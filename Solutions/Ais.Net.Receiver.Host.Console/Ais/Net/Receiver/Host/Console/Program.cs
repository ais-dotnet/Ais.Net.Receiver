// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

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

namespace Ais.Net.Receiver.Host.Console;

/// <summary>
/// Host application for the <see cref="ReceiverHost"/>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Entry point for the application.
    /// </summary>
    /// <returns>Task representing the operation.</returns>
    public static async Task Main()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("settings.json", true, true)
            .AddJsonFile("settings.local.json", true, true)
            .Build();

        AisConfig? aisConfig = config.GetSection("Ais").Get<AisConfig>();
        StorageConfig? storageConfig = config.GetSection("Storage").Get<StorageConfig>();

        if (aisConfig is null || storageConfig is null)
        {
            throw new InvalidOperationException("Configuration is invalid.");
        }

        INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
            aisConfig.Host,
            aisConfig.Port,
            aisConfig.RetryPeriodicity,
            retryAttemptLimit: aisConfig.RetryAttempts);

        // If you wanted to run from a captured stream uncomment this line:

        /*
        INmeaReceiver receiver = new FileStreamNmeaReceiver(@"PATH-TO-RECORDING.nm4");
        */

        ReceiverHost receiverHost = new(receiver);

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
        {
            receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
                        .Subscribe(statistics =>
                                   System.Console.WriteLine($"{DateTime.UtcNow.ToUniversalTime()}: Sentences: {statistics.Sentence} | Messages: {statistics.Message} | Errors: {statistics.Error}"));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
        {
            receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
            {
                (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGround ?? 0}]");
                System.Console.ResetColor();
            });
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
        {
            // Write out the messages as they are received over the wire.
            receiverHost.Sentences.Subscribe(System.Console.WriteLine);
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
        {
            receiverHost.Messages.Subscribe(System.Console.WriteLine);

            // Write out errors in the console
            receiverHost.Errors.Subscribe(error =>
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"Error received: {error.Exception.Message}");
                System.Console.WriteLine($"Bad line: {error.Line}");
                System.Console.ResetColor();
            });
        }

        if (storageConfig.EnableCapture)
        {
            IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);
            BatchBlock<string> batchBlock = new(storageConfig.WriteBatchSize);
            ActionBlock<IEnumerable<string>> actionBlock = new(storageClient.PersistAsync);
            batchBlock.LinkTo(actionBlock);

            // Persist the messages as they are received over the wire.
            receiverHost.Sentences.Subscribe(batchBlock.AsObserver());
        }

        CancellationTokenSource cts = new();

        Task task = receiverHost.StartAsync(cts.Token);

        // If you wanted to cancel the long-running process:
        /* cts.Cancel(); */

        await task;
    }
}