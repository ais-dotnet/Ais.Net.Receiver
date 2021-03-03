// <copyright file="ReceiverHost.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Ais.Net.Receiver.Configuration;
    using Ais.Net.Receiver.Domain;
    using Ais.Net.Receiver.Parser;
    using Ais.Net.Receiver.Storage;

    public class ReceiverHost
    {
        private readonly AisConfig configuration;
        private readonly IStorageClient storageClient;
        private readonly NmeaReceiver receiver;

        public ReceiverHost(AisConfig configuration, IStorageClient storageClient)
        {
            this.configuration = configuration;
            this.storageClient = storageClient;

            this.receiver = new NmeaReceiver(
                this.configuration.Host,
                this.configuration.Port,
                this.configuration.RetryPeriodicity,
                this.configuration.RetryAttempts);
        }

        public async Task StartAsync()
        {
            var processor = new NmeaToAisTelemetryStreamProcessor();
            var adapter = new NmeaLineToAisStreamAdapter(processor);

            //processor.Telemetry.Subscribe(this.OnTelemetryReceived);

            IObservable<IGroupedObservable<uint, AisMessageBase>> byVessel = processor.Telemetry.GroupBy(m => m.Mmsi);
            var xs =
                from vg in byVessel
                let vesselLocations = vg.OfType<IVesselPosition>()
                let vesselNames = vg.OfType<IVesselName>()
                let vesselLocationsWithNames = vesselLocations.CombineLatest(vesselNames)
                from locationAndName in vesselLocationsWithNames
                select (mmsi: vg.Key, location: locationAndName.First, name: locationAndName.Second);

            xs.Subscribe(ln =>
                {
                    (uint mmsi, IVesselPosition position, IVesselName name) = ln;
                    string positionText = position.Position is null ? "unknown position" : $"{position.Position.Latitude},{position.Position.Longitude}";
                    Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}]");
                });

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

        private void OnTelemetryReceived(AisMessageBase message)
        {
            if (message is IVesselPosition position)
            {
                string positionText = position.Position is null ? "unknown position" : $"{position.Position.Latitude},{position.Position.Longitude}";
                Console.WriteLine($"[{message.MessageType}] [{message.Mmsi}] [{positionText}]");

            }
            else
            {
                Console.WriteLine($"[{message.MessageType}] [{message.Mmsi}]");
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