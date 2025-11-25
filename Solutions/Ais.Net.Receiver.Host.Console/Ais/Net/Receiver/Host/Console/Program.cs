// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ais.Net.Receiver.Host.Console;

/// <summary>
/// Host application for the <see cref="ReceiverHost"/>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Task representing the operation.</returns>
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile("settings.json", true, true);
        builder.Configuration.AddJsonFile("settings.local.json", true, true);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Ais.Net.Receiver.Console"))
            .WithMetrics(metrics => metrics
                .AddMeter("Ais.Net.Receiver.Console")
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddSource("Ais.Net.Receiver.Console")
                .AddSource("Ais.Net.Receiver")
                .AddOtlpExporter());

        using IHost host = builder.Build();
        IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

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

        await using ReceiverHost receiverHost = new(receiver);
        using ReceiverTelemetry telemetry = new("Ais.Net.Receiver.Console");
        telemetry.Bind(receiverHost);

        CompositeDisposable subscriptions = [];

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
        {
            subscriptions.Add(
                receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
                            .Subscribe(
                                statistics =>
                                       System.Console.WriteLine($"{DateTime.UtcNow.ToUniversalTime()}: Sentences: {statistics.Sentence} | Messages: {statistics.Message} | Errors: {statistics.Error}"),
                                error => System.Console.WriteLine($"Error in statistics stream: {error.Message}")));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
        {
            subscriptions.Add(
                receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
                {
                    (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                    string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGround ?? 0}]");
                    System.Console.ResetColor();
                }));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
        {
            // Write out the messages as they are received over the wire.
            subscriptions.Add(receiverHost.Sentences.Subscribe(System.Console.WriteLine));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
        {
            subscriptions.Add(receiverHost.Messages.Subscribe(System.Console.WriteLine));

            // Write out errors in the console
            subscriptions.Add(
                receiverHost.Errors.Subscribe(error =>
                {
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"Error received: {error.Exception.Message}");
                    System.Console.WriteLine($"Bad line: {error.Line}");
                    System.Console.ResetColor();
                }));
        }

        if (storageConfig.EnableCapture)
        {
            IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);
            BatchBlock<string> batchBlock = new(storageConfig.WriteBatchSize);
            ActionBlock<IEnumerable<string>> actionBlock = new(storageClient.PersistAsync);
            batchBlock.LinkTo(actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

            // Persist the messages as they are received over the wire.
            subscriptions.Add(receiverHost.Sentences.Subscribe(batchBlock.AsObserver()));

            // Ensure we wait for the storage to finish flushing
            _ = actionBlock.Completion.ContinueWith(_ => System.Console.WriteLine("Storage flush completed."));
        }

        using CancellationTokenSource cts = new();

        System.Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            System.Console.WriteLine("Stopping...");
            cts.Cancel();
        };

        try
        {
            await receiverHost.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        finally
        {
            subscriptions.Dispose();
        }
    }
}