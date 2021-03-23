// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Host.Console
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Dataflow;

    using Ais.Net.Models;
    using Ais.Net.Models.Abstractions;
    using Ais.Net.Receiver.Configuration;
    using Ais.Net.Receiver.Receiver;
    using Ais.Net.Receiver.Storage;
    using Ais.Net.Receiver.Storage.Azure.Blob;
    using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

    using Microsoft.Extensions.Configuration;

    using NDepend.Path;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("settings.local.json", true, true)
                            .Build();

            AisConfig aisConfig = config.GetSection("Ais").Get<AisConfig>();
            StorageConfig storageConfig = config.GetSection("Storage").Get<StorageConfig>();

            IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);

            var receiverHost = new ReceiverHost(aisConfig);

            receiverHost.Messages.Subscribe(x => 
            {
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds: x.Item2.Value);
                var file = @$"C:\Temp\nmea-ais\projection\vessel-by-id\{x.Item1.Mmsi}\{timestamp:yyyy}\{timestamp:MM}\{timestamp:dd}\{timestamp:yyyyMMddTHH}.nm4".ToAbsoluteFilePath();
                
                if (!file.Exists)
                {
                    Directory.CreateDirectory(file.ParentDirectoryPath.ToString());
                }

                File.AppendAllText(file.ToString(), Encoding.ASCII.GetString(x.Item3) + Environment.NewLine);
                
                Console.WriteLine(file);
            });

            /*            // Decode teh sentences into messages, and group by the vessel by Id
                        IObservable<IGroupedObservable<uint, IAisMessage>> byVessel = receiverHost.Messages.Select(m => m.Item1).GroupBy(m => m.Mmsi);

                        // Combine the various message types required to create a stream containing name and navigation
                        IObservable<(uint mmsi, IVesselNavigation navigation, IVesselName name)>? vesselNavigationWithNameStream =
                            from perVesselMessages in byVessel
                            let vesselNavigationUpdates = perVesselMessages.OfType<IVesselNavigation>()
                            let vesselNames = perVesselMessages.OfType<IVesselName>()
                            let vesselLocationsWithNames = vesselNavigationUpdates.CombineLatest(vesselNames, (navigation, name) => (navigation, name))
                            from vesselLocationAndName in vesselLocationsWithNames
                            select (mmsi: perVesselMessages.Key, vesselLocationAndName.navigation, vesselLocationAndName.name);

                        vesselNavigationWithNameStream.Subscribe(navigationWithName =>
                        {
                            (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                            string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGroundDegrees ?? 0}]");
                            Console.ResetColor();
                        });

                        var batchBlock = new BatchBlock<string>(storageConfig.WriteBatchSize);
                        var actionBlock = new ActionBlock<IEnumerable<string>>(storageClient.PersistAsync);

                        batchBlock.LinkTo(actionBlock);

                        // Write out the messages as they are received over the wire.
                        receiverHost.Sentences.Subscribe(sentence => Console.WriteLine(sentence));

                        // Persist the messages as they are received over the wire.
                        receiverHost.Sentences.Subscribe(batchBlock.AsObserver());*/

            var cts = new CancellationTokenSource();

            Task task = receiverHost.StartAsync(cts.Token);

            // If you wanted to cancel the long running process:
            // cts.Cancel();

            await task;
        }
    }
}