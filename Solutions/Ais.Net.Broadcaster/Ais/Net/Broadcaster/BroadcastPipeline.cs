// <copyright file="BroadcastPipeline.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Broadcaster.Telemetry;
using Ais.Net.Receiver.Parser;
using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Broadcaster;

/// <summary>
/// Pumps sentences from an <see cref="INmeaReceiver"/> source into one or more
/// <see cref="INmeaBroadcaster"/> sinks, applying pacing, looping and fault injection per
/// <see cref="BroadcastPipelineOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// For each sentence the pipeline first applies pacing, then the time-based faults (disconnect,
/// then silence), then the count-based substitutions (malformed, then over-long), and finally
/// hands the line to every sink in turn. All timing goes through the supplied
/// <see cref="TimeProvider"/>, so a fake time provider drives the pipeline deterministically.
/// </para>
/// <para>
/// The counters are safe to read from another thread while <see cref="RunAsync"/> is running.
/// </para>
/// </remarks>
public sealed class BroadcastPipeline
{
    /// <summary>
    /// The length of the line sent by <see cref="FaultInjectionOptions.OverLongEveryN"/>: longer
    /// than the receivers' 8 KiB line cap.
    /// </summary>
    internal const int OverLongLineLength = 9000;

    /// <summary>
    /// A <c>c:</c> value at or above this is taken to be Unix milliseconds rather than seconds
    /// (Unix seconds will not reach it until the year 5138).
    /// </summary>
    private const long MillisecondTimestampThreshold = 100_000_000_000;

    private static readonly ReadOnlyMemory<byte> OverLongLine = CreateOverLongLine();

    private readonly INmeaReceiver source;
    private readonly INmeaBroadcaster[] sinks;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;

    private long sentencesBroadcast;
    private long faultsInjected;
    private int loops;
    private int running;

    // Pacing and fault state; touched only by the RunAsync loop.
    private long sentenceNumber;
    private long fixedScheduleStart;
    private long fixedScheduleIndex;
    private long previousTagTimestampMs;
    private long previousTagReleaseTimestamp;
    private long lastDisconnectTimestamp;
    private long lastSilenceTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="BroadcastPipeline"/> class.
    /// </summary>
    /// <param name="source">The source of sentences. For <see cref="BroadcastPipelineOptions.Loop"/>
    /// it must be re-enumerable from the start, as a finite (file) <see cref="NmeaReceiver"/> is.</param>
    /// <param name="sinks">The broadcasters to fan sentences out to; at least one.</param>
    /// <param name="options">Pacing, looping and fault injection settings.</param>
    /// <param name="timeProvider">The time provider used for pacing and fault timers.</param>
    /// <param name="logger">An optional logger.</param>
    public BroadcastPipeline(
        INmeaReceiver source,
        IReadOnlyList<INmeaBroadcaster> sinks,
        BroadcastPipelineOptions options,
        TimeProvider timeProvider,
        ILogger<BroadcastPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (sinks.Count == 0)
        {
            throw new ArgumentException("At least one sink is required.", nameof(sinks));
        }

        if (sinks.Any(s => s is null))
        {
            throw new ArgumentException("Sinks must not contain null.", nameof(sinks));
        }

        if (options.Faults is null)
        {
            throw new ArgumentException("Faults must not be null.", nameof(options));
        }

        if (options.Pacing == PacingMode.Fixed && !(options.SentencesPerSecond > 0 && double.IsFinite(options.SentencesPerSecond)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.SentencesPerSecond, "SentencesPerSecond must be a positive, finite number for fixed pacing.");
        }

        if (options.Pacing == PacingMode.TagBlockTimestamps && !(options.SpeedFactor > 0 && double.IsFinite(options.SpeedFactor)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.SpeedFactor, "SpeedFactor must be a positive, finite number for tag-block pacing.");
        }

        if (options.Faults.MalformedEveryN is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Faults.MalformedEveryN, "MalformedEveryN must be positive.");
        }

        if (options.Faults.OverLongEveryN is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Faults.OverLongEveryN, "OverLongEveryN must be positive.");
        }

        if (options.Faults.DisconnectEvery is { } disconnectEvery && disconnectEvery <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), disconnectEvery, "DisconnectEvery must be positive.");
        }

        if (options.Faults.SilenceEvery is { } silenceEvery && silenceEvery <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), silenceEvery, "SilenceEvery must be positive.");
        }

        this.source = source;
        this.sinks = [.. sinks];
        this.Options = options;
        this.timeProvider = timeProvider;
        this.logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Gets the line sent in place of a sentence by <see cref="FaultInjectionOptions.MalformedEveryN"/>:
    /// a truncated payload whose checksum does not match.
    /// </summary>
    public static ReadOnlyMemory<byte> MalformedLine { get; } = "!AIVDM,1,1,,A,13u?etPv2;0n:dD,0*00"u8.ToArray();

    /// <summary>
    /// Gets the pacing, looping and fault injection settings.
    /// </summary>
    public BroadcastPipelineOptions Options { get; }

    /// <summary>
    /// Gets the sinks sentences are fanned out to.
    /// </summary>
    public IReadOnlyList<INmeaBroadcaster> Sinks => this.sinks;

    /// <summary>
    /// Gets the number of lines handed to the sinks, including lines substituted by fault
    /// injection. A line handed to several sinks counts once.
    /// </summary>
    public long SentencesBroadcast => Interlocked.Read(ref this.sentencesBroadcast);

    /// <summary>
    /// Gets the number of faults injected so far, of every kind: disconnects, silences, malformed
    /// lines and over-long lines.
    /// </summary>
    public long FaultsInjected => Interlocked.Read(ref this.faultsInjected);

    /// <summary>
    /// Gets the number of times a finite source has been restarted from the beginning.
    /// </summary>
    public int Loops => Volatile.Read(ref this.loops);

    /// <summary>
    /// Gets the total number of clients currently connected across every sink.
    /// </summary>
    public int SinkClientCount
    {
        get
        {
            int total = 0;
            foreach (INmeaBroadcaster sink in this.sinks)
            {
                total += sink.ClientCount;
            }

            return total;
        }
    }

    /// <summary>
    /// Starts every sink, pumps the source into them until the source ends (or, with
    /// <see cref="BroadcastPipelineOptions.Loop"/>, until cancelled), and then stops every sink.
    /// </summary>
    /// <param name="cancellationToken">Stops the run. Cancellation ends the run quietly, without
    /// throwing.</param>
    /// <returns>A task that completes when the run has ended and the sinks have been stopped. It
    /// faults with the source's (or a sink's) exception if the run failed.</returns>
    /// <exception cref="InvalidOperationException">The pipeline is already running.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref this.running, 1) != 0)
        {
            throw new InvalidOperationException("The pipeline is already running.");
        }

        try
        {
            this.logger.PipelineStarted(string.Join(", ", this.sinks.Select(s => s.Name)), this.Options.Pacing);

            foreach (INmeaBroadcaster sink in this.sinks)
            {
                await sink.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            long now = this.timeProvider.GetTimestamp();
            this.fixedScheduleStart = now;
            this.fixedScheduleIndex = 0;
            this.lastDisconnectTimestamp = now;
            this.lastSilenceTimestamp = now;

            while (!cancellationToken.IsCancellationRequested)
            {
                this.previousTagTimestampMs = 0;
                long linesThisPass = 0;

                await foreach (ReadOnlyMemory<byte> line in this.source.GetAsync(cancellationToken).ConfigureAwait(false))
                {
                    linesThisPass++;
                    await this.ProcessAsync(line, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                this.logger.SourceEnded(this.SentencesBroadcast);

                if (!this.Options.Loop)
                {
                    break;
                }

                if (linesThisPass == 0)
                {
                    // Re-enumerating an empty source would spin without ever yielding.
                    this.logger.EmptySourceNotLooped();
                    break;
                }

                int loop = Interlocked.Increment(ref this.loops);
                this.logger.Looping(loop);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is a normal way to end a run.
        }
        catch (Exception ex)
        {
            this.logger.SourceError(ex);
            throw;
        }
        finally
        {
            foreach (INmeaBroadcaster sink in this.sinks)
            {
                try
                {
                    await sink.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Keep stopping the remaining sinks, and never mask the run's own outcome.
                    this.logger.SinkStopFailed(ex, sink.Name);
                }
            }

            this.logger.PipelineStopped(this.SentencesBroadcast, this.FaultsInjected, this.Loops);
            Volatile.Write(ref this.running, 0);
        }
    }

    private static ReadOnlyMemory<byte> CreateOverLongLine()
    {
        byte[] line = new byte[OverLongLineLength];
        line.AsSpan().Fill((byte)'A');
        return line;
    }

    private async ValueTask ProcessAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        await this.PaceAsync(line, cancellationToken).ConfigureAwait(false);

        FaultInjectionOptions faults = this.Options.Faults;

        if (faults.DisconnectEvery is { } disconnectEvery &&
            this.timeProvider.GetElapsedTime(this.lastDisconnectTimestamp) >= disconnectEvery)
        {
            this.RecordFault("disconnect");
            foreach (INmeaBroadcaster sink in this.sinks)
            {
                await sink.DisconnectAllAsync(cancellationToken).ConfigureAwait(false);
            }

            this.lastDisconnectTimestamp = this.timeProvider.GetTimestamp();
        }

        if (faults.SilenceEvery is { } silenceEvery &&
            this.timeProvider.GetElapsedTime(this.lastSilenceTimestamp) >= silenceEvery)
        {
            this.RecordFault("silence");
            if (faults.SilenceDuration > TimeSpan.Zero)
            {
                await Task.Delay(faults.SilenceDuration, this.timeProvider, cancellationToken).ConfigureAwait(false);
            }

            long now = this.timeProvider.GetTimestamp();
            this.lastSilenceTimestamp = now;

            // Resume the fixed schedule from here, with this sentence in its first slot, rather
            // than bursting to catch up on the silence.
            this.fixedScheduleStart = now;
            this.fixedScheduleIndex = 1;
        }

        long number = ++this.sentenceNumber;
        ReadOnlyMemory<byte> toSend = line;
        if (faults.MalformedEveryN is { } malformedEveryN && number % malformedEveryN == 0)
        {
            this.RecordFault("malformed");
            toSend = MalformedLine;
        }
        else if (faults.OverLongEveryN is { } overLongEveryN && number % overLongEveryN == 0)
        {
            this.RecordFault("overlong");
            toSend = OverLongLine;
        }

        foreach (INmeaBroadcaster sink in this.sinks)
        {
            await sink.BroadcastAsync(toSend, cancellationToken).ConfigureAwait(false);
        }

        Interlocked.Increment(ref this.sentencesBroadcast);
    }

    private async ValueTask PaceAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        switch (this.Options.Pacing)
        {
            case PacingMode.Fixed:
            {
                // Sentence k is due k / rate after the schedule started, so per-sentence overheads
                // do not accumulate into drift; sleep only when ahead of schedule.
                long index = this.fixedScheduleIndex++;
                TimeSpan due = TimeSpan.FromTicks((long)(index * (TimeSpan.TicksPerSecond / this.Options.SentencesPerSecond)));
                TimeSpan elapsed = this.timeProvider.GetElapsedTime(this.fixedScheduleStart);
                if (due > elapsed)
                {
                    await Task.Delay(due - elapsed, this.timeProvider, cancellationToken).ConfigureAwait(false);
                }

                break;
            }

            case PacingMode.TagBlockTimestamps:
            {
                long timestamp = line.Span.ParseNmeaBlockTags().UnixTimestamp;
                if (timestamp <= 0)
                {
                    // No usable timestamp: send immediately, leaving the previous one as the baseline.
                    break;
                }

                long timestampMs = timestamp >= MillisecondTimestampThreshold ? timestamp : timestamp * 1000;
                if (this.previousTagTimestampMs != 0)
                {
                    TimeSpan gap = TimeSpan.FromMilliseconds((timestampMs - this.previousTagTimestampMs) / this.Options.SpeedFactor);
                    if (gap < TimeSpan.Zero)
                    {
                        gap = TimeSpan.Zero;
                    }
                    else if (gap > this.Options.MaxTagBlockGap)
                    {
                        gap = this.Options.MaxTagBlockGap;
                    }

                    // Measured from when the previous timestamped sentence was released, so time
                    // spent between the two (sinks, untimestamped sentences) is not added on top.
                    TimeSpan elapsed = this.timeProvider.GetElapsedTime(this.previousTagReleaseTimestamp);
                    if (gap > elapsed)
                    {
                        await Task.Delay(gap - elapsed, this.timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                }

                this.previousTagTimestampMs = timestampMs;
                this.previousTagReleaseTimestamp = this.timeProvider.GetTimestamp();
                break;
            }

            default:
                break;
        }
    }

    private void RecordFault(string kind)
    {
        Interlocked.Increment(ref this.faultsInjected);
        this.logger.FaultInjected(kind);
    }
}
