// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class ReceiverHost
    {
        private readonly AisConfig configuration;
        private readonly IStorageClient storageClient;
        private NmeaReceiver receiver;

        public ReceiverHost(AisConfig configuration, IStorageClient storageClient)
        {
            this.configuration = configuration;
            this.storageClient = storageClient;
        }

        public async Task StartAsync()
        {
            this.receiver = new NmeaReceiver(
                this.configuration.Host,
                this.configuration.Port,
                this.configuration.RetryPeriodicity,
                this.configuration.RetryAttempts);

            var processor = new NmeaToAisTelemetryStreamProcessor();
            var adapter = new NmeaLineToAisStreamAdapter(processor);

            processor.Telemetry.Subscribe(OnTelemetryReceived);
            //processor.Telemetry.CombineLatest()

            await foreach (var message in this.GetAsync())
            {
                static void ProcessLineNonAsync(string msg, INmeaLineStreamProcessor lineStreamProcessor)
                {
                    var asciiMessage = Encoding.ASCII.GetBytes(msg);
                    var line = new NmeaLineParser(asciiMessage);
                    lineStreamProcessor.OnNext(line, 0);
                }

                ProcessLineNonAsync(message, adapter);
            }

            // await messages.Buffer(int.Parse(this.configuration[ConfigurationKeys.WriteBatchSize])).Select(this.storageClient.PersistAsync);
        }

        private void OnTelemetryReceived(AisTelmetry message)
        {
            Console.WriteLine($"{message.VesselName?.Trim('@').Trim()} [{message.Mmsi}] [{message.Position?.Latitude},{message.Position?.Longitude}]");
        }

        public IObserver<AisTelmetry> TelemetryStream { get; set; }

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