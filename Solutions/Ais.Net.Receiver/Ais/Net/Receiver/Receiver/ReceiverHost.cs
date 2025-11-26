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

using Corvus.Retry;
using Corvus.Retry.Policies;
using Corvus.Retry.Strategies;

namespace Ais.Net.Receiver.Receiver;

public class ReceiverHost : IAsyncDisposable
{
    private static readonly ActivitySource ActivitySource = new("Ais.Net.Receiver");
    private readonly INmeaReceiver receiver;
    private readonly TimeSpan retryPeriodicity;
    private readonly Subject<string> sentences = new();
    private readonly Subject<IAisMessage> messages = new();
    private readonly Subject<Metadata> metadata = new();
    private readonly Subject<(Exception Exception, string Line)> errors = new();

    public ReceiverHost(INmeaReceiver receiver, TimeSpan? retryPeriodicity = null)
    {
        this.receiver = receiver;
        this.retryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(5);
    }

    public IObservable<string> Sentences => this.sentences;

    public IObservable<IAisMessage> Messages => this.messages;

    public IObservable<Metadata> Metadata => this.metadata;

    public IObservable<(Exception Exception, string Line)> Errors => this.errors;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Retriable.RetryAsync(() =>
                this.StartAsyncInternal(cancellationToken),
                cancellationToken,
                new Linear(periodicity: this.retryPeriodicity, maxTries: 100),
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

            using Activity? activity = ActivitySource.StartActivity("ProcessMessage");

            static void ProcessLineNonAsync(ReadOnlyMemory<byte> line, INmeaLineStreamProcessor lineStreamProcessor, Subject<(Exception Exception, string Line)> errorSubject)
            {
                try
                {
                    lineStreamProcessor.OnNext(new NmeaLineParser(line.Span), lineNumber: 0);
                }
                catch (ArgumentException ex)
                {
                    Activity.Current?.AddException(ex);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error);

                    if (errorSubject.HasObservers)
                    {
                        errorSubject.OnNext((Exception: ex, Encoding.ASCII.GetString(line.Span)));
                    }
                }
                catch (NotImplementedException ex)
                {
                    Activity.Current?.AddException(ex);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error);

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
                ProcessLineNonAsync(message, adapter, this.errors);
            }
        }

        this.sentences.OnCompleted();
        this.messages.OnCompleted();
        this.metadata.OnCompleted();
        this.errors.OnCompleted();
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        await foreach (ReadOnlyMemory<byte> message in this.receiver.GetAsync(cancellationToken))
        {
            yield return message.Span.IsMissingNmeaBlockTags ? message.PrependNmeaBlockTags() : message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.sentences.Dispose();
        this.messages.Dispose();
        this.metadata.Dispose();
        this.errors.Dispose();

        if (this.receiver is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (this.receiver is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}