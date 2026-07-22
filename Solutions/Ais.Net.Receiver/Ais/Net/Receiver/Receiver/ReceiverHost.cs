// <copyright file="ReceiverHost.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Parser;
using Ais.Net.Receiver.Resilience;
using Ais.Net.Receiver.Telemetry;

using Polly;

namespace Ais.Net.Receiver.Receiver;

public class ReceiverHost : IAsyncDisposable
{
    private static readonly ActivitySource DefaultActivitySource = new("Ais.Net.Receiver");
    private readonly INmeaReceiver receiver;
    private readonly ResiliencePipeline retryPipeline;
    private readonly TimeProvider timeProvider;
    private readonly ActivitySource activitySource;
    private readonly ApplicationMetrics? metrics;
    private readonly Subject<string> sentences = new();
    private readonly Subject<ReadOnlyMemory<byte>> rawSentences = new();
    private readonly Subject<IAisMessage> messages = new();
    private readonly Subject<Metadata> metadata = new();
    private readonly Subject<(Exception Exception, string Line)> errors = new();

    // Reusable scratch buffer for prepending NMEA tag blocks. The receive loop pulls and fully
    // processes one message before the next is produced, so a single buffer avoids a per-message
    // allocation without any aliasing hazard (see GetAsync).
    private byte[] prependBuffer = new byte[256];

    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiverHost"/> class.
    /// </summary>
    /// <param name="receiver">The NMEA receiver.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="retryPeriodicity">The retry periodicity.</param>
    /// <param name="retryAttempts">The number of retry attempts.</param>
    /// <param name="metrics">The application metrics.</param>
    public ReceiverHost(INmeaReceiver receiver, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttempts = 100, ApplicationMetrics? metrics = null)
        : this(receiver, timeProvider, null, retryPeriodicity, retryAttempts, metrics)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiverHost"/> class with custom instrumentation.
    /// </summary>
    /// <param name="receiver">The NMEA receiver.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="instrumentation">The application instrumentation for tracing.</param>
    /// <param name="retryPeriodicity">The retry periodicity.</param>
    /// <param name="retryAttempts">The number of retry attempts.</param>
    /// <param name="metrics">The application metrics.</param>
    public ReceiverHost(
        INmeaReceiver receiver,
        TimeProvider timeProvider,
        ApplicationInstrumentation? instrumentation,
        TimeSpan? retryPeriodicity = null,
        int retryAttempts = 100,
        ApplicationMetrics? metrics = null)
    {
        this.receiver = receiver;
        this.timeProvider = timeProvider;
        this.activitySource = instrumentation?.ActivitySource ?? DefaultActivitySource;
        this.metrics = metrics;
        this.retryPipeline = RetryPipelines.ConstantDelay(retryPeriodicity ?? TimeSpan.FromSeconds(5), retryAttempts);
    }

    public IObservable<string> Sentences => this.sentences;

    /// <summary>
    /// Gets the raw (undecoded) NMEA sentence bytes. Prefer this over <see cref="Sentences"/> for
    /// byte-oriented consumers such as storage capture and counting, to avoid materialising a
    /// string per line.
    /// </summary>
    public IObservable<ReadOnlyMemory<byte>> RawSentences => this.rawSentences;

    public IObservable<IAisMessage> Messages => this.messages;

    public IObservable<Metadata> Metadata => this.metadata;

    public IObservable<(Exception Exception, string Line)> Errors => this.errors;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await this.retryPipeline.ExecuteAsync(
            async token => await this.StartAsyncInternal(token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StartAsyncInternal(CancellationToken cancellationToken = default)
    {
        using NmeaToAisMessageTypeProcessor processor = new();
        NmeaLineToAisStreamAdapter adapter = new(processor);
        using CompositeDisposable methodSubscriptions = [];

        (int StationId, long UnixTimestamp) currentMetadata = (0, 0);

        methodSubscriptions.Add(processor.Messages.Subscribe(message =>
        {
            this.messages.OnNext(message);

            if (this.metadata.HasObservers)
            {
                this.metadata.OnNext(new Metadata(currentMetadata.StationId, currentMetadata.UnixTimestamp, message));
            }
        }));

        methodSubscriptions.Add(processor.ParseErrors.Subscribe(this.errors));

        await foreach (ReadOnlyMemory<byte> message in this.GetAsync(cancellationToken))
        {
            currentMetadata = message.Span.ParseNmeaBlockTags();

            using Activity? activity = this.activitySource.StartActivity("ProcessMessage");

            // Enrich activity with station metadata
            activity?.SetStationMetadata(currentMetadata.StationId, currentMetadata.UnixTimestamp);

            if (this.rawSentences.HasObservers)
            {
                this.rawSentences.OnNext(message.ToArray());
            }

            if (this.sentences.HasObservers)
            {
                this.sentences.OnNext(Encoding.ASCII.GetString(message.Span));
            }

            if (this.messages.HasObservers || this.metadata.HasObservers)
            {
                long startTimestamp = Stopwatch.GetTimestamp();
                ProcessLineNonAsync(message, adapter, this.errors);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                this.metrics?.MessageProcessingDuration.Record(elapsed.TotalMilliseconds);
            }
        }

        this.sentences.OnCompleted();
        this.rawSentences.OnCompleted();
        this.messages.OnCompleted();
        this.metadata.OnCompleted();
        this.errors.OnCompleted();
    }

    private static void ProcessLineNonAsync(ReadOnlyMemory<byte> line, INmeaLineStreamProcessor lineStreamProcessor, Subject<(Exception Exception, string Line)> errorSubject)
    {
        try
        {
            lineStreamProcessor.OnNext(new NmeaLineParser(line.Span), lineNumber: 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A single malformed sentence must not propagate out of the receive loop: that would
            // fault StartAsyncInternal and trigger a full reconnect plus retry backoff - a long stall
            // caused by one bad line. Instead, categorise the failure, surface it as a per-message
            // error, and carry on with the next line. Known-shaped failures keep their existing error
            // types; anything else (e.g. an IndexOutOfRangeException from a truncated payload) is a
            // parse error.
            string errorType = ex switch
            {
                NotImplementedException => "unsupported_message",
                _ => "parse_error",
            };

            Activity.Current?.SetErrorType(errorType);
            Activity.Current?.RecordExceptionWithStatus(ex, escaped: true);

            if (errorSubject.HasObservers)
            {
                errorSubject.OnNext((Exception: ex, Encoding.ASCII.GetString(line.Span)));
            }
        }
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ReadOnlyMemory<byte> message in this.receiver.GetAsync(cancellationToken))
        {
            if (!message.Span.IsMissingNmeaBlockTags)
            {
                yield return message;
                continue;
            }

            // Prepend the NMEA tag block into a reusable buffer instead of allocating a fresh
            // array per message. This is safe because each yielded item is consumed synchronously
            // and completely within a single iteration of the StartAsyncInternal loop before the
            // next item is pulled (there is no await in that loop body, and the span-based Ais.Net
            // parser cannot retain the buffer), so the buffer is never read after it is reused.
            int required = message.Length + NmeaMessageExtensions.MaxPrefixLength;
            if (this.prependBuffer.Length < required)
            {
                this.prependBuffer = new byte[Math.Max(required, this.prependBuffer.Length * 2)];
            }

            int written = message.Span.PrependNmeaBlockTags(this.timeProvider, this.prependBuffer);
            yield return this.prependBuffer.AsMemory(0, written);
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.sentences.Dispose();
        this.rawSentences.Dispose();
        this.messages.Dispose();
        this.metadata.Dispose();
        this.errors.Dispose();

        await this.receiver.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
