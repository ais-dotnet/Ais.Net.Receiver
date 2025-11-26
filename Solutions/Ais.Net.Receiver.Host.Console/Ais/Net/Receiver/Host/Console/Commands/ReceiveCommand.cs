// <copyright file="ReceiveCommand.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading.Tasks.Dataflow;
using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage;
using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ais.Net.Receiver.Host.Console.Commands;

public class ReceiveCommand : AsyncCommand<ReceiveCommand.Settings>
{
    private readonly AisConfig aisConfig;
    private readonly StorageConfig storageConfig;
    private readonly IServiceProvider serviceProvider;

    public class Settings : CommandSettings
    {
    }

    public ReceiveCommand(
        IOptions<AisConfig> aisConfig,
        IOptions<StorageConfig> storageConfig,
        IServiceProvider serviceProvider)
    {
        this.aisConfig = aisConfig.Value;
        this.storageConfig = storageConfig.Value;
        this.serviceProvider = serviceProvider;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Start hosted services (like OpenTelemetry)
        IEnumerable<IHostedService> hostedServices = this.serviceProvider.GetServices<IHostedService>();
        foreach (IHostedService service in hostedServices)
        {
            await service.StartAsync(cancellationToken);
        }

        try
        {
            return await RunReceiverAsync(cancellationToken);
        }
        finally
        {
            foreach (IHostedService service in hostedServices)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private async Task<int> RunReceiverAsync(CancellationToken cancellationToken)
    {
        INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
            this.aisConfig.Host,
            this.aisConfig.Port,
            this.aisConfig.RetryPeriodicity,
            retryAttemptLimit: this.aisConfig.RetryAttempts);

        await using ReceiverHost receiverHost = new(receiver);
        using ReceiverTelemetry telemetry = new("Ais.Net.Receiver.Console");
        telemetry.Bind(receiverHost);

        using CompositeDisposable subscriptions = [];
        BatchBlock<string>? batchBlock = null;
        ActionBlock<IEnumerable<string>>? actionBlock = null;
        Timer? batchTimer = null;

        if (this.aisConfig.LoggerVerbosity == LogLevel.Warning)
        {
            subscriptions.Add(
                receiverHost.GetStreamStatistics(this.aisConfig.StatisticsPeriodicity)
                    .Subscribe(
                        statistics =>
                            AnsiConsole.MarkupLine($"[grey]{DateTime.UtcNow.ToUniversalTime():s}[/]: Sentences: [cyan]{statistics.Sentence}[/] | Messages: [cyan]{statistics.Message}[/] | Errors: [red]{statistics.Error}[/]"),
                        error => AnsiConsole.MarkupLine($"[red]Error in statistics stream: {Markup.Escape(error.Message)}[/]")));
        }

        if (this.aisConfig.LoggerVerbosity == LogLevel.Information)
        {
            subscriptions.Add(
                receiverHost.Messages.VesselNavigationWithNameStream(this.aisConfig.VesselInactivityTimeout).Subscribe(navigationWithName =>
                {
                    (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                    string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                    AnsiConsole.MarkupLine($"[green][[{mmsi}: '{Markup.Escape(name.VesselName.CleanVesselName())}' ]] - [[{Markup.Escape(positionText)}]] - [[{navigation.CourseOverGround ?? 0}]][/]");
                }));
        }

        if (this.aisConfig.LoggerVerbosity == LogLevel.Debug)
        {
            subscriptions.Add(receiverHost.Sentences.Subscribe(sentence => AnsiConsole.WriteLine(sentence)));
        }

        if (this.aisConfig.LoggerVerbosity == LogLevel.Trace)
        {
            subscriptions.Add(receiverHost.Messages.Subscribe(message => AnsiConsole.WriteLine(message.ToString() ?? string.Empty)));

            subscriptions.Add(
                receiverHost.Errors.Subscribe(error =>
                {
                    AnsiConsole.MarkupLine($"[red]Error received: {Markup.Escape(error.Exception.Message)}[/]");
                    AnsiConsole.MarkupLine($"[red]Bad line: {Markup.Escape(error.Line)}[/]");
                }));
        }

        if (this.storageConfig.EnableCapture)
        {
            IStorageClient storageClient = new AzureAppendBlobStorageClient(this.storageConfig);

            batchBlock = new(
                this.storageConfig.WriteBatchSize,
                new() { BoundedCapacity = this.storageConfig.BoundedCapacity });

            actionBlock = new(
                async batch =>
                {
                    try
                    {
                        await storageClient.PersistAsync(batch);
                    }
                    catch (Exception ex)
                    {
                        Activity.Current?.AddException(ex);
                        Activity.Current?.SetStatus(ActivityStatusCode.Error);
                        AnsiConsole.MarkupLine($"[red]Storage error: {Markup.Escape(ex.Message)}[/]");
                    }
                },
                new() { MaxDegreeOfParallelism = this.storageConfig.MaxDegreeOfParallelism });

            batchBlock.LinkTo(actionBlock, new() { PropagateCompletion = true });

            batchTimer = new(
                _ => batchBlock?.TriggerBatch(),
                null,
                TimeSpan.FromSeconds(this.storageConfig.BatchTimeoutSeconds),
                TimeSpan.FromSeconds(this.storageConfig.BatchTimeoutSeconds));

            subscriptions.Add(receiverHost.Sentences.Subscribe(batchBlock.AsObserver()));
        }

        // Handle Ctrl+C gracefully
        try
        {
            await receiverHost.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
            AnsiConsole.MarkupLine("[yellow]Stopping...[/]");
        }
        finally
        {
            batchTimer?.Dispose();

            if (batchBlock is not null && actionBlock is not null)
            {
                batchBlock.Complete();
                try
                {
                    using CancellationTokenSource flushCts = new(TimeSpan.FromSeconds(30));
                    await actionBlock.Completion.WaitAsync(flushCts.Token);
                    AnsiConsole.MarkupLine("[green]Storage flush completed.[/]");
                }
                catch (OperationCanceledException)
                {
                    AnsiConsole.MarkupLine("[yellow]Storage flush timeout.[/]");
                }
                catch (Exception ex)
                {
                    Activity.Current?.AddException(ex);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error);
                    AnsiConsole.MarkupLine($"[red]Storage flush error: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        return 0;
    }
}