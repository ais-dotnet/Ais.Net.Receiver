// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Host.Console
{
    using System;
    using System.Collections.Generic;
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

    public static class Program
    {
        public static async Task Main()
        {
            IConfiguration config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("settings.local.json", true, true)
                            .Build();

            AisConfig aisConfig = config.GetSection("Ais").Get<AisConfig>();
            StorageConfig storageConfig = config.GetSection("Storage").Get<StorageConfig>();
            
            INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
                aisConfig.Host,
                aisConfig.Port,
                aisConfig.RetryPeriodicity,
                aisConfig.RetryAttempts);

            // If you wanted to run from a captured stream:
            //INmeaReceiver receiver = new FileStreamNmeaReceiver(@"PATH-TO-RECORDING.nm4");

            var receiverHost = new ReceiverHost(receiver);

            if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
            {
                receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
                    .Subscribe(stat => Console.WriteLine($"{DateTime.UtcNow.ToUniversalTime()}: Sentences: {stat.sentence} | Messages: {stat.message} | Errors {stat.error}"));
            }

            if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
            {
                receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
                {
                    (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                    string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";
                    
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGroundDegrees ?? 0}]");
                    Console.ResetColor();
                });
            }
            
            if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
            {
                // Write out the messages as they are received over the wire.
                receiverHost.Sentences.Subscribe(Console.WriteLine);
            }

            if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
            {
                receiverHost.Messages.Subscribe(Console.WriteLine);

                // Write out errors in the console
                receiverHost.Errors.Subscribe(error =>
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error received: {error.Exception.Message}");
                    Console.WriteLine($"Bad line: {error.Line}");
                    Console.ResetColor();
                });
            }

            if (storageConfig.EnableCapture)
            {
                IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);
                var batchBlock = new BatchBlock<string>(storageConfig.WriteBatchSize);
                var actionBlock = new ActionBlock<IEnumerable<string>>(storageClient.PersistAsync);
                batchBlock.LinkTo(actionBlock);

                // Persist the messages as they are received over the wire.
                receiverHost.Sentences.Subscribe(batchBlock.AsObserver());
            }

            var cts = new CancellationTokenSource();

            Task task = receiverHost.StartAsync(cts.Token);

            // If you wanted to cancel the long running process:
            // cts.Cancel();

            await task;
        }
    }
}