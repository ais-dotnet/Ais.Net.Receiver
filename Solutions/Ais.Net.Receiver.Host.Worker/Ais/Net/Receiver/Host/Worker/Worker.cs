// <copyright file="Worker.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Disposables;

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Health;
using Ais.Net.Receiver.Hosting;
using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Host.Worker;

public class Worker : BackgroundService, IHostedLifecycleService, IAsyncDisposable
{
    private readonly ILogger<Worker> logger;
    private readonly IOptionsMonitor<AisConfig> aisOptionsMonitor;
    private readonly IOptionsMonitor<StorageConfig> storageOptionsMonitor;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics metrics;
    private readonly ApplicationInstrumentation instrumentation;
    private readonly IAisConnectionMonitor connectionMonitor;

    private ReceiverHost? receiverHost;
    private CompositeDisposable? subscriptions;
    private StorageBatchPipeline? storagePipeline;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<AisConfig> aisOptionsMonitor,
        IOptionsMonitor<StorageConfig> storageOptionsMonitor,
        TimeProvider timeProvider,
        ApplicationMetrics metrics,
        ApplicationInstrumentation instrumentation,
        IAisConnectionMonitor connectionMonitor)
    {
        this.logger = logger;
        this.aisOptionsMonitor = aisOptionsMonitor;
        this.storageOptionsMonitor = storageOptionsMonitor;
        this.timeProvider = timeProvider;
        this.metrics = metrics;
        this.instrumentation = instrumentation;
        this.connectionMonitor = connectionMonitor;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        this.logger.WorkerStarting();

        this.receiverHost = ReceiverPipeline.CreateHost(
            this.aisOptionsMonitor.CurrentValue,
            this.timeProvider,
            this.instrumentation,
            this.metrics,
            // Drive connection health from the receiver's real TCP state rather than worker lifetime.
            onConnectionStateChanged: this.connectionMonitor.RecordConnectionStateChanged);

        this.subscriptions = [];

        this.SetupMetricsSubscriptions();
        this.SetupLoggingSubscriptions();
        this.SetupStorageIfEnabled();

        this.logger.WorkerInitializationComplete();

        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        this.logger.WorkerStarted();
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        this.logger.WorkerStopping();
        this.connectionMonitor.RecordConnectionStateChanged(false);
        return Task.CompletedTask;
    }

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        this.logger.WorkerStopped();

        this.subscriptions?.Dispose();

        if (this.storagePipeline is not null)
        {
            await this.storagePipeline.FlushAsync(
                TimeSpan.FromSeconds(30),
                onCompleted: this.logger.StorageFlushCompleted,
                onTimedOut: this.logger.StorageFlushTimedOut,
                onError: this.logger.StorageFlushError);
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.subscriptions?.Dispose();

        if (this.storagePipeline is not null)
        {
            await this.storagePipeline.DisposeAsync();
        }

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
            this.logger.ReceiverHostNotInitialized();
            return;
        }

        try
        {
            await this.receiverHost.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation - graceful shutdown
            this.logger.WorkerCancelled();
        }
    }

    private void SetupMetricsSubscriptions()
    {
        if (this.receiverHost is null || this.subscriptions is null)
        {
            return;
        }

        // Subscribe to messages for metrics with message type dimension
        this.subscriptions.Add(
            this.receiverHost.Messages.Subscribe(msg =>
            {
                this.metrics.MessagesReceived.Add(
                    1,
                    new KeyValuePair<string, object?>("ais.message_type", msg.MessageType));
                this.connectionMonitor.RecordMessageReceived();
            }));

        // Subscribe to sentences for metrics
        this.subscriptions.Add(
            this.receiverHost.RawSentences.Subscribe(_ =>
            {
                this.metrics.SentencesReceived.Add(1);
            }));

        // Subscribe to errors for metrics with error type dimension
        this.subscriptions.Add(
            this.receiverHost.Errors.Subscribe(error =>
            {
                string errorType = error.Exception switch
                {
                    NotImplementedException => "unsupported_message",
                    _ => "parse_error"
                };
                this.metrics.ErrorsReceived.Add(
                    1,
                    new KeyValuePair<string, object?>("error.type", errorType));
            }));
    }

    private void SetupLoggingSubscriptions()
    {
        if (this.receiverHost is null || this.subscriptions is null)
        {
            return;
        }

        AisConfig aisConfig = this.aisOptionsMonitor.CurrentValue;

        if (aisConfig.Telemetry.Verbosity == LogLevel.Warning)
        {
            this.subscriptions.Add(
                this.receiverHost.GetStreamStatistics(aisConfig.Telemetry.StatisticsPeriodicity)
                    .Subscribe(
                        statistics =>
                            this.logger.StreamStatistics(
                                this.timeProvider.GetUtcNow().UtcDateTime,
                                statistics.Sentence,
                                statistics.Message,
                                statistics.Error),
                        error => this.logger.StatisticsStreamError(error)));
        }

        if (aisConfig.Telemetry.Verbosity == LogLevel.Information)
        {
            this.subscriptions.Add(
                this.receiverHost.Messages.VesselNavigationWithNameStream(aisConfig.Telemetry.VesselInactivityTimeout).Subscribe(navigationWithName =>
                {
                    (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                    string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                    if (this.logger.IsEnabled(LogLevel.Information))
                    {
                        this.logger.VesselNavigation(
                            mmsi,
                            name.VesselName.CleanVesselName(),
                            positionText,
                            navigation.CourseOverGround ?? 0);
                    }
                }));
        }

        if (aisConfig.Telemetry.Verbosity == LogLevel.Debug)
        {
            this.subscriptions.Add(
                this.receiverHost.Sentences.Subscribe(s =>
                {
                    if (this.logger.IsEnabled(LogLevel.Debug))
                    {
                        this.logger.SentenceReceived(s);
                    }
                }));
        }

        if (aisConfig.Telemetry.Verbosity == LogLevel.Trace)
        {
            this.subscriptions.Add(
                this.receiverHost.Messages.Subscribe(m =>
                {
                    if (this.logger.IsEnabled(LogLevel.Trace))
                    {
                        this.logger.MessageReceived(m.ToString() ?? string.Empty);
                    }
                }));

            this.subscriptions.Add(
                this.receiverHost.Errors.Subscribe(error =>
                {
                    if (this.logger.IsEnabled(LogLevel.Error))
                    {
                        this.logger.ErrorReceived(error.Exception.Message);
                        this.logger.BadLine(error.Line);
                    }
                }));
        }
    }

    private void SetupStorageIfEnabled()
    {
        if (this.receiverHost is null)
        {
            return;
        }

        // The batching, backpressure, and shutdown-flush logic is shared with the console host via
        // ReceiverPipeline; this host supplies only its structured-logging callbacks.
        this.storagePipeline = ReceiverPipeline.CreateStorage(
            this.storageOptionsMonitor.CurrentValue,
            this.receiverHost.RawSentences,
            this.timeProvider,
            this.metrics,
            this.instrumentation,
            this.logger,
            onPersistError: this.logger.StoragePersistenceFailed,
            onSentencesDropped: this.logger.SentencesDropped);
    }
}