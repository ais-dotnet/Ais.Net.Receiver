// <copyright file="ReceiverHost.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
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
                        false);
        }

        private async Task StartAsyncInternal(CancellationToken cancellationToken = default)
        {
            var processor = new NmeaToAisMessageTypeProcessor();
            var adapter = new NmeaLineToAisStreamAdapter(processor);

            processor.Messages.Subscribe(this.messages);

            await foreach (string? message in this.GetAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                static void ProcessLineNonAsync(string line, INmeaLineStreamProcessor lineStreamProcessor, Subject<(Exception Exception, string Line)> errorSubject)
                {
                    byte[]? lineAsAscii = Encoding.ASCII.GetBytes(line);

                    try
                    {
                        lineStreamProcessor.OnNext(new NmeaLineParser(lineAsAscii), 0);
                    }
                    catch (ArgumentException ex)
                    {
                        if (errorSubject.HasObservers)
                        {
                            errorSubject.OnNext((ex, line));
                        }
                    }
                    catch (NotImplementedException ex)
                    {
                        if (errorSubject.HasObservers)
                        {
                            errorSubject.OnNext((ex, line));
                        }
                    }
                }

                this.sentences.OnNext(message);

                if (this.messages.HasObservers)
                {
                    ProcessLineNonAsync(message, adapter, this.errors);
                }
            }
        }

        private async IAsyncEnumerable<string> GetAsync([EnumeratorCancellation]CancellationToken cancellationToken = default)
        {
            await foreach (string? message in this.receiver.GetAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                if (message.IsMissingNmeaBlockTags())
                {
                    yield return message.PrependNmeaBlockTags();
                }
                else
                {
                    yield return message;
                }
            }
        }
    }
}