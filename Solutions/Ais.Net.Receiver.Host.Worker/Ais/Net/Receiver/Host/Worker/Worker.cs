// <copyright file="Worker.cs" company="Endjin Limited">
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Host.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> logger;
    private readonly IConfiguration config;

    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AisConfig? aisConfig = this.config.GetSection("Ais").Get<AisConfig>();
        StorageConfig? storageConfig = this.config.GetSection("Storage").Get<StorageConfig>();

        if (aisConfig is null || storageConfig is null)
        {
            this.logger.LogCritical("Configuration is invalid.");
            return;
        }

        INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
            aisConfig.Host,
            aisConfig.Port,
            aisConfig.RetryPeriodicity,
            retryAttemptLimit: aisConfig.RetryAttempts);

        ReceiverHost receiverHost = new(receiver);
        using ReceiverTelemetry telemetry = new("Ais.Net.Receiver");
        telemetry.Bind(receiverHost);

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
        {
            receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
                        .Subscribe(
                            statistics =>
                            System.Console.WriteLine($"{DateTime.UtcNow.ToUniversalTime()}: Sentences: {statistics.Sentence} | Messages: {statistics.Message} | Errors: {statistics.Error}"),
                            error => this.logger.LogError(error, "Error in statistics stream"));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
        {
            receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
            {
                (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                if (this.logger.IsEnabled(LogLevel.Information))
                {
                    this.logger.LogInformation(
                        "[{Mmsi}: '{VesselName}'] - [{Position}] - [{CourseOverGround}]",
                        mmsi,
                        name.VesselName.CleanVesselName(),
                        positionText,
                        navigation.CourseOverGround ?? 0);
                }
            });
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
        {
            receiverHost.Sentences.Subscribe(s =>
            {
                if (this.logger.IsEnabled(LogLevel.Information))
                {
                    this.logger.LogInformation("{Sentence}", s);
                }
            });
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
        {
            receiverHost.Messages.Subscribe(m =>
            {
                if (this.logger.IsEnabled(LogLevel.Information))
                {
                    this.logger.LogInformation("{Message}", m.ToString());
                }
            });

            receiverHost.Errors.Subscribe(error =>
            {
                if (this.logger.IsEnabled(LogLevel.Error))
                {
                    this.logger.LogError("Error received: {Message}", error.Exception.Message);
                    this.logger.LogError("Bad line: {Line}", error.Line);
                }
            });
        }

        if (storageConfig.EnableCapture)
        {
            IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);
            BatchBlock<string> batchBlock = new(storageConfig.WriteBatchSize);
            ActionBlock<IEnumerable<string>> actionBlock = new(storageClient.PersistAsync);
            batchBlock.LinkTo(actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

            receiverHost.Sentences.Subscribe(batchBlock.AsObserver());
            _ = actionBlock.Completion.ContinueWith(_ => this.logger.LogInformation("Storage flush completed."), stoppingToken);
        }

        try
        {
            await receiverHost.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
    }
}