// <copyright file="ReceiverHost.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using Ais.Net.Models.Abstractions;
    using Ais.Net.Receiver.Configuration;
    using Ais.Net.Receiver.Parser;

    using System;
    using System.Collections.Generic;
    using System.Reactive.Subjects;
    using System.Text;
    using System.Threading.Tasks;

    public class ReceiverHost
    {
        private readonly AisConfig configuration;
        private readonly NmeaReceiver receiver;
        private readonly Subject<IAisMessage> telemetry = new();

        public IObservable<IAisMessage> Telemetry => this.telemetry;

        public ReceiverHost(AisConfig configuration)
        {
            this.configuration = configuration;
            this.receiver = new NmeaReceiver(
                this.configuration.Host,
                this.configuration.Port,
                this.configuration.RetryPeriodicity,
                this.configuration.RetryAttempts);
        }

        public async Task StartAsync()
        {
            var processor = new NmeaToAisMessageTypeProcessor();
            var adapter = new NmeaLineToAisStreamAdapter(processor);

            processor.Telemetry.Subscribe(this.telemetry);

            await foreach (var message in this.GetAsync())
            {
                static void ProcessLineNonAsync(string line, INmeaLineStreamProcessor lineStreamProcessor)
                {
                    var lineAsAscii = Encoding.ASCII.GetBytes(line);
                    lineStreamProcessor.OnNext(new NmeaLineParser(lineAsAscii), 0);
                }

                ProcessLineNonAsync(message, adapter);
            }
        }

        public async IAsyncEnumerable<string> GetAsync()
        {
            await foreach (var message in this.receiver.GetAsync())
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