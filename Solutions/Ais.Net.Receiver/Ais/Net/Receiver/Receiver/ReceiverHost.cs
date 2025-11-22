// <copyright file="ReceiverHost.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Parser;

using Corvus.Retry;
using Corvus.Retry.Policies;
using Corvus.Retry.Strategies;

namespace Ais.Net.Receiver.Receiver;

public class ReceiverHost
{
    private readonly INmeaReceiver receiver;
    private readonly Subject<string> sentences = new();
    private readonly Subject<IAisMessage> messages = new();
    private readonly Subject<(Exception Exception, string Line)> errors = new();

    public ReceiverHost(INmeaReceiver receiver)
    {
        this.receiver = receiver;
    }

    public IObservable<string> Sentences => this.sentences;

    public IObservable<IAisMessage> Messages => this.messages;

    public IObservable<(Exception Exception, string Line)> Errors => this.errors;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Retriable.RetryAsync(() =>
                this.StartAsyncInternal(cancellationToken),
                cancellationToken,
                new Backoff(maxTries: 100, deltaBackoff: TimeSpan.FromSeconds(5)),
                new AnyExceptionPolicy(),
                continueOnCapturedContext: false);
    }

    private async Task StartAsyncInternal(CancellationToken cancellationToken = default)
    {
        NmeaToAisMessageTypeProcessor processor = new();
        NmeaLineToAisStreamAdapter adapter = new(processor);

        processor.Messages.Subscribe(this.messages);
        processor.ParseErrors.Subscribe(this.errors);

        await foreach (ReadOnlyMemory<byte> message in this.GetAsync(cancellationToken))
        {
            static void ProcessLineNonAsync(ReadOnlyMemory<byte> line, INmeaLineStreamProcessor lineStreamProcessor, Subject<(Exception Exception, string Line)> errorSubject)
            {
                try
                {
                    lineStreamProcessor.OnNext(new NmeaLineParser(line.Span), lineNumber: 0);
                }
                catch (ArgumentException ex)
                {
                    if (errorSubject.HasObservers)
                    {
                        errorSubject.OnNext((Exception: ex, Encoding.ASCII.GetString(line.Span)));
                    }
                }
                catch (NotImplementedException ex)
                {
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

            if (this.messages.HasObservers)
            {
                ProcessLineNonAsync(message, adapter, this.errors);
            }
        }

        this.sentences.OnCompleted();
        this.messages.OnCompleted();
        this.errors.OnCompleted();
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        await foreach (ReadOnlyMemory<byte> message in this.receiver.GetAsync(cancellationToken))
        {
            yield return message.Span.IsMissingNmeaBlockTags() ? message.PrependNmeaBlockTags() : message;
        }
    }
}