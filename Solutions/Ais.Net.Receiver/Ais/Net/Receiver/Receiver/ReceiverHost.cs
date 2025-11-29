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
using Ais.Net.Receiver.Telemetry;

using Corvus.Retry;
using Corvus.Retry.Policies;
using Corvus.Retry.Strategies;

namespace Ais.Net.Receiver.Receiver;

public class ReceiverHost : IAsyncDisposable
{
    private static readonly ActivitySource DefaultActivitySource = new("Ais.Net.Receiver");
    private readonly INmeaReceiver receiver;
    private readonly TimeSpan retryPeriodicity;
    private readonly int retryAttempts;
    private readonly TimeProvider timeProvider;
    private readonly ActivitySource activitySource;
    private readonly ApplicationMetrics? metrics;
    private readonly Subject<string> sentences = new();
    private readonly Subject<IAisMessage> messages = new();
    private readonly Subject<Metadata> metadata = new();
    private readonly Subject<(Exception Exception, string Line)> errors = new();

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
        this.retryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(5);
        this.retryAttempts = retryAttempts;
    }

    public IObservable<string> Sentences => this.sentences;

    public IObservable<IAisMessage> Messages => this.messages;

    public IObservable<Metadata> Metadata => this.metadata;

    public IObservable<(Exception Exception, string Line)> Errors => this.errors;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Retriable.RetryAsync(
            () => this.StartAsyncInternal(cancellationToken),
            cancellationToken,
            new Linear(periodicity: this.retryPeriodicity, maxTries: this.retryAttempts),
            new AnyExceptionPolicy(),
            continueOnCapturedContext: false);
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

            void ProcessLineNonAsync(ReadOnlyMemory<byte> line, INmeaLineStreamProcessor lineStreamProcessor, Subject<(Exception Exception, string Line)> errorSubject)
            {
                try
                {
                    lineStreamProcessor.OnNext(new NmeaLineParser(line.Span), lineNumber: 0);
                }
                catch (ArgumentException ex)
                {
                    Activity.Current?.SetErrorType("parse_error");
                    Activity.Current?.RecordExceptionWithStatus(ex, escaped: true);

                    if (errorSubject.HasObservers)
                    {
                        errorSubject.OnNext((Exception: ex, Encoding.ASCII.GetString(line.Span)));
                    }
                }
                catch (NotImplementedException ex)
                {
                    Activity.Current?.SetErrorType("unsupported_message");
                    Activity.Current?.RecordExceptionWithStatus(ex, escaped: true);

                    if (errorSubject.HasObservers)
                    {
                        errorSubject.OnNext((Exception: ex, Encoding.ASCII.GetString(line.Span)));
                    }
                }
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
        this.messages.OnCompleted();
        this.metadata.OnCompleted();
        this.errors.OnCompleted();
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ReadOnlyMemory<byte> message in this.receiver.GetAsync(cancellationToken))
        {
            yield return message.Span.IsMissingNmeaBlockTags ? message.PrependNmeaBlockTags(this.timeProvider) : message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.sentences.Dispose();
        this.messages.Dispose();
        this.metadata.Dispose();
        this.errors.Dispose();

        await this.receiver.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
