// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Transport-agnostic NMEA receiver. Connects through an <see cref="INmeaConnection"/> and streams
/// its lines. For a <see cref="NmeaSourceKind.Live"/> connection it reconnects with linear backoff
/// on failure, end of stream, or idle timeout; for a <see cref="NmeaSourceKind.Finite"/> connection
/// it reads the source once and completes at end of stream.
/// </summary>
public sealed class NmeaReceiver : INmeaReceiver
{
    // Fallback idle-read timeout when none is configured. Detects a silently dropped (half-open) TCP
    // connection without being so short that a legitimately quiet feed churns reconnects. Deliberately
    // independent of the reconnect knobs, which are unrelated and (with the shipped 1s x 5) would
    // otherwise make this only ~5 seconds.
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(60);

    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics? metrics;
    private readonly ApplicationInstrumentation? instrumentation;
    private readonly ILogger logger;
    private readonly Action<bool>? onConnectionStateChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="NmeaReceiver"/> class.
    /// </summary>
    /// <param name="connection">The connection to read from. The receiver owns it and disposes it.</param>
    /// <param name="timeProvider">The time provider used for idle-timeout bookkeeping.</param>
    /// <param name="options">Reconnect and idle-detection settings; defaults apply when omitted.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="logger">An optional logger.</param>
    /// <param name="onConnectionStateChanged">Optional callback reporting the live connection state.</param>
    /// <param name="instrumentation">Optional tracing instrumentation.</param>
    public NmeaReceiver(INmeaConnection connection, TimeProvider timeProvider, NmeaReceiverOptions? options = null, ApplicationMetrics? metrics = null, ILogger? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
    {
        this.Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.Options = options ?? new NmeaReceiverOptions();
        this.metrics = metrics;
        this.instrumentation = instrumentation;
        this.logger = logger ?? NullLogger.Instance;

        // Reports the live connection state (true on connect, false on any disconnect/idle/error),
        // so health reflects the real connection rather than the host's start/stop lifetime.
        this.onConnectionStateChanged = onConnectionStateChanged;
    }

    /// <summary>
    /// Gets the connection this receiver reads from.
    /// </summary>
    public INmeaConnection Connection { get; }

    /// <summary>
    /// Gets the reconnect and idle-detection settings.
    /// </summary>
    public NmeaReceiverOptions Options { get; }

    /// <summary>
    /// Streams NMEA lines from the connection. For a <see cref="NmeaSourceKind.Live"/> connection
    /// this reconnects with linear backoff on failure or idle timeout and only ends on
    /// cancellation; for a <see cref="NmeaSourceKind.Finite"/> connection it ends at end of stream,
    /// and a failure to connect propagates to the caller. This is a native pull-based sequence: a
    /// yielded buffer is only guaranteed valid until the next <c>MoveNextAsync</c> (the underlying
    /// connection reuses its read buffer), which is safe here because each line is consumed
    /// synchronously before the next is requested.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>The sequence of NMEA lines.</returns>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default) =>
        this.Connection.Kind == NmeaSourceKind.Finite
            ? this.GetFiniteAsync(cancellationToken)
            : this.GetLiveAsync(cancellationToken);

    /// <summary>
    /// Backwards-compatible observable projection over <see cref="GetAsync"/>. Each line is copied
    /// so that observers - whose lifetimes this method does not control - receive an owned buffer.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>An observable of NMEA lines.</returns>
    public IObservable<ReadOnlyMemory<byte>> GetObservable(CancellationToken cancellationToken = default) =>
        Observable.Create<ReadOnlyMemory<byte>>(async (observer, innerCancellation) =>
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancellation);
            try
            {
                await foreach (ReadOnlyMemory<byte> line in this.GetAsync(cts.Token).ConfigureAwait(false))
                {
                    observer.OnNext(line.ToArray());
                }

                observer.OnCompleted();
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                observer.OnError(ex);
            }
        });

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.Connection.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a finite source once: connect (failures propagate), read until end of stream, stop.
    /// None of the live-feed machinery applies - no idle timeout, no backoff, and no
    /// connected-but-silent failure accounting, because an empty file is not an outage.
    /// </summary>
    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetFiniteAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        bool connected = false;

        try
        {
            this.metrics?.ConnectionAttempts.Add(1);
            this.logger.StreamConnecting(this.Connection.EndpointAddress, this.Connection.EndpointPort);

            try
            {
                await this.Connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            this.logger.StreamConnected(this.Connection.EndpointAddress, this.Connection.EndpointPort);
            connected = true;
            this.onConnectionStateChanged?.Invoke(true);

            while (!cancellationToken.IsCancellationRequested)
            {
                ReadOnlyMemory<byte>? line;
                try
                {
                    line = await this.Connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (line is null)
                {
                    yield break;
                }

                yield return line.Value;
            }
        }
        finally
        {
            await this.Connection.DisposeAsync().ConfigureAwait(false);

            if (connected)
            {
                this.onConnectionStateChanged?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// Streams NMEA lines from a live feed, reconnecting with linear backoff on failure or idle
    /// timeout.
    /// </summary>
    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetLiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int retryAttempt = 0;

        // Mirrors retryAttempt into the consecutive-failure gauge. Tracked separately so the gauge
        // can be wound back to zero on connect without assuming what it currently reads.
        long reportedConsecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            bool connected = false;

            // A span per connection attempt: this is the low-frequency, high-value event in this loop,
            // and it gives the connect/disconnect transitions somewhere to live.
            using (Activity? connectActivity = this.instrumentation?.ActivitySource.StartActivity("Connect"))
            {
                connectActivity?.SetTag(SemanticConventions.Network.ServerAddress, this.Connection.EndpointAddress);
                connectActivity?.SetTag(SemanticConventions.Network.ServerPort, this.Connection.EndpointPort);
                connectActivity?.SetTag(SemanticConventions.Network.Transport, this.Connection.Transport);
                if (this.Connection.ProtocolName is { } protocolName)
                {
                    connectActivity?.SetTag(SemanticConventions.Network.ProtocolName, protocolName);
                }

                connectActivity?.SetTag(SemanticConventions.Connection.Attempt, retryAttempt);

                try
                {
                    this.metrics?.ConnectionAttempts.Add(1);
                    this.logger.StreamConnecting(this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    await this.Connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    this.logger.StreamConnected(this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    retryAttempt = 0; // Reset retry count on successful connection

                    // Note: the consecutive-failure gauge is deliberately NOT reset here. Connecting
                    // proves the port accepts, not that the feed sends; it is reset on the first
                    // delivered sentence instead.
                    connected = true;
                    connectActivity?.RecordConnectionStateEvent(true, "connect_succeeded", this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    this.onConnectionStateChanged?.Invoke(true);
                }
                catch (OperationCanceledException)
                {
                    connectActivity?.RecordConnectionStateEvent(false, "shutdown", this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    yield break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this.metrics?.ConnectionFailures.Add(1);
                    this.metrics?.ConsecutiveConnectionFailures.Add(1);
                    reportedConsecutiveFailures++;
                    this.logger.StreamConnectionError(ex, this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    connectActivity?.RecordExceptionWithStatus(ex, escaped: false);
                    connectActivity?.SetErrorType("connect_failed");
                    connectActivity?.RecordConnectionStateEvent(false, "connect_failed", this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    this.onConnectionStateChanged?.Invoke(false);
                }
            }

            if (connected)
            {
                TimeSpan idleTimeout = this.Options.IdleTimeout is { } configured && configured > TimeSpan.Zero
                    ? configured
                    : DefaultIdleTimeout;
                TimeSpan minResetInterval = idleTimeout / 2;
                long lastResetTimestamp = this.timeProvider.GetTimestamp();

                // A successful connect is not evidence of a working feed: a server that accepts and
                // then immediately closes, or accepts and stays silent, reconnects cleanly forever
                // while delivering nothing. Health is therefore tracked against delivered data, not
                // against connect success.
                bool deliveredData = false;
                bool failureCounted = false;
                string endReason = "stream_ended";

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(idleTimeout);

                try
                {
                    while (this.Connection.Connected && !cancellationToken.IsCancellationRequested)
                    {
                        ReadOnlyMemory<byte>? line;
                        try
                        {
                            line = await this.Connection.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            // Idle timeout - break to reconnect.
                            this.logger.StreamIdleTimeout(idleTimeout.TotalSeconds, this.Connection.EndpointAddress, this.Connection.EndpointPort);
                            endReason = "idle_timeout";
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            // External cancellation - stop (the outer check ends the loop after disposal).
                            endReason = "shutdown";
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Read/connection error - break to reconnect.
                            this.metrics?.ConnectionFailures.Add(1);
                            failureCounted = true;
                            this.logger.StreamConnectionError(ex, this.Connection.EndpointAddress, this.Connection.EndpointPort);
                            endReason = "read_error";
                            break;
                        }

                        if (line is null)
                        {
                            // End of stream - break to reconnect.
                            break;
                        }

                        if (!deliveredData)
                        {
                            // First sentence of this connection: the feed is demonstrably live, so
                            // wind the consecutive-failure gauge back to zero.
                            deliveredData = true;

                            if (reportedConsecutiveFailures > 0)
                            {
                                this.metrics?.ConsecutiveConnectionFailures.Add(-reportedConsecutiveFailures);
                                reportedConsecutiveFailures = 0;
                            }
                        }

                        yield return line.Value;

                        TimeSpan elapsed = this.timeProvider.GetElapsedTime(lastResetTimestamp);
                        if (elapsed > minResetInterval)
                        {
                            timeoutCts.CancelAfter(idleTimeout);
                            lastResetTimestamp = this.timeProvider.GetTimestamp();
                        }
                    }
                }
                finally
                {
                    await this.Connection.DisposeAsync().ConfigureAwait(false);

                    // A connection that ended without ever yielding a sentence is an outage, even
                    // though connect succeeded. Count it so the consecutive-failure gauge - and the
                    // alert built on it - sees accept-then-close and accept-then-silent feeds.
                    // Cancellation is excluded: that is a shutdown, not a failure.
                    if (!deliveredData && !cancellationToken.IsCancellationRequested)
                    {
                        if (!failureCounted)
                        {
                            this.metrics?.ConnectionFailures.Add(1);
                        }

                        this.metrics?.ConsecutiveConnectionFailures.Add(1);
                        reportedConsecutiveFailures++;
                        this.logger.StreamConnectedButSilent(this.Connection.EndpointAddress, this.Connection.EndpointPort);

                        if (endReason == "stream_ended")
                        {
                            endReason = "connected_but_silent";
                        }
                    }

                    // Record the closing transition with why it happened, so a trace shows whether the
                    // feed ended cleanly, stalled, errored, or was shut down.
                    using (Activity? closeActivity = this.instrumentation?.ActivitySource.StartActivity("ConnectionClosed"))
                    {
                        closeActivity?.RecordConnectionStateEvent(false, endReason, this.Connection.EndpointAddress, this.Connection.EndpointPort);
                    }

                    // The connection ended (disconnect, idle timeout, read error, or cancellation).
                    this.onConnectionStateChanged?.Invoke(false);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Linear backoff based on RetryPeriodicity.
            retryAttempt++;
            int cappedRetryAttempt = Math.Min(retryAttempt, this.Options.RetryAttemptLimit);
            TimeSpan delay = TimeSpan.FromTicks(this.Options.RetryPeriodicity.Ticks * cappedRetryAttempt);

            this.metrics?.RetryAttempts.Add(
                1,
                new KeyValuePair<string, object?>("component", "connection"));

            this.logger.StreamConnectionRetry(retryAttempt, this.Connection.EndpointAddress, this.Connection.EndpointPort, (long)delay.TotalMilliseconds);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
