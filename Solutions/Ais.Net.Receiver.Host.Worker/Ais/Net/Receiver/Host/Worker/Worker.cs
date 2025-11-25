// <copyright file="Worker.cs" company="Endjin Limited">
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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Host.Worker;

public class Worker : BackgroundService, IHostedLifecycleService, IAsyncDisposable
{
    private readonly ILogger<Worker> logger;
    private readonly IOptionsMonitor<AisConfig> aisOptionsMonitor;
    private readonly IOptionsMonitor<StorageConfig> storageOptionsMonitor;

    private ReceiverHost? receiverHost;
    private ReceiverTelemetry? telemetry;
    private CompositeDisposable? subscriptions;
    private BatchBlock<string>? batchBlock;
    private ActionBlock<IEnumerable<string>>? actionBlock;
    private IStorageClient? storageClient;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<AisConfig> aisOptionsMonitor,
        IOptionsMonitor<StorageConfig> storageOptionsMonitor)
    {
        this.logger = logger;
        this.aisOptionsMonitor = aisOptionsMonitor;
        this.storageOptionsMonitor = storageOptionsMonitor;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Worker starting - initializing components");

        AisConfig aisConfig = this.aisOptionsMonitor.CurrentValue;

        INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
            aisConfig.Host,
            aisConfig.Port,
            aisConfig.RetryPeriodicity,
            retryAttemptLimit: aisConfig.RetryAttempts);

        this.receiverHost = new ReceiverHost(receiver);
        this.telemetry = new ReceiverTelemetry("Ais.Net.Receiver");
        this.telemetry.Bind(this.receiverHost);

        this.subscriptions = [];

        this.SetupSubscriptions();
        this.SetupStorageIfEnabled();

        this.logger.LogInformation("Worker initialization complete");
        
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Worker started");
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Worker stopping");
        return Task.CompletedTask;
    }

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Worker stopped - completing dataflow pipeline");

        // Complete the dataflow pipeline and wait for it to finish
        if (this.batchBlock is not null && this.actionBlock is not null)
        {
            this.batchBlock.Complete();
            try
            {
                await this.actionBlock.Completion.WaitAsync(cancellationToken);
                this.logger.LogInformation("Storage flush completed");
            }
            catch (OperationCanceledException)
            {
                this.logger.LogWarning("Storage flush cancelled during shutdown");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.subscriptions?.Dispose();
        this.telemetry?.Dispose();

        if (this.receiverHost is not null)
        {
            await this.receiverHost.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this.receiverHost is null)
        {
            this.logger.LogCritical("ReceiverHost not initialized - cannot execute");
            return;
        }

        try
        {
            await this.receiverHost.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation - graceful shutdown
            this.logger.LogInformation("Worker execution cancelled");
        }
    }

    private void SetupSubscriptions()
    {
        if (this.receiverHost is null || this.subscriptions is null)
        {
            return;
        }

        AisConfig aisConfig = this.aisOptionsMonitor.CurrentValue;

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
        {
            this.subscriptions.Add(
                this.receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
                    .Subscribe(
                        statistics =>
                            this.logger.LogInformation(
                                "{Timestamp:s}: Sentences: {Sentences} | Messages: {Messages} | Errors: {Errors}",
                                DateTime.UtcNow.ToUniversalTime(),
                                statistics.Sentence,
                                statistics.Message,
                                statistics.Error),
                        error => this.logger.LogError(error, "Error in statistics stream")));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
        {
            this.subscriptions.Add(
                this.receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
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
                }));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
        {
            this.subscriptions.Add(
                this.receiverHost.Sentences.Subscribe(s =>
                {
                    if (this.logger.IsEnabled(LogLevel.Information))
                    {
                        this.logger.LogInformation("{Sentence}", s);
                    }
                }));
        }

        if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
        {
            this.subscriptions.Add(
                this.receiverHost.Messages.Subscribe(m =>
                {
                    if (this.logger.IsEnabled(LogLevel.Information))
                    {
                        this.logger.LogInformation("{Message}", m.ToString());
                    }
                }));

            this.subscriptions.Add(
                this.receiverHost.Errors.Subscribe(error =>
                {
                    if (this.logger.IsEnabled(LogLevel.Error))
                    {
                        this.logger.LogError("Error received: {Message}", error.Exception.Message);
                        this.logger.LogError("Bad line: {Line}", error.Line);
                    }
                }));
        }
    }

    private void SetupStorageIfEnabled()
    {
        StorageConfig storageConfig = this.storageOptionsMonitor.CurrentValue;

        if (!storageConfig.EnableCapture || this.receiverHost is null || this.subscriptions is null)
        {
            return;
        }

        this.storageClient = new AzureAppendBlobStorageClient(storageConfig);
        this.batchBlock = new BatchBlock<string>(storageConfig.WriteBatchSize);
        this.actionBlock = new ActionBlock<IEnumerable<string>>(this.storageClient.PersistAsync);
        this.batchBlock.LinkTo(this.actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

        this.subscriptions.Add(this.receiverHost.Sentences.Subscribe(this.batchBlock.AsObserver()));
    }
}