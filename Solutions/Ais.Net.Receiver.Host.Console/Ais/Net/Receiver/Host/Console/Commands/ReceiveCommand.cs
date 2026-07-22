// <copyright file="ReceiveCommand.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Disposables;
using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Hosting;
using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Telemetry;
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
    private readonly TimeProvider timeProvider;

    public class Settings : CommandSettings
    {
    }

    public ReceiveCommand(
        IOptions<AisConfig> aisConfig,
        IOptions<StorageConfig> storageConfig,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider)
    {
        this.aisConfig = aisConfig.Value;
        this.storageConfig = storageConfig.Value;
        this.serviceProvider = serviceProvider;
        this.timeProvider = timeProvider;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Start hosted services (like OpenTelemetry)
        IEnumerable<IHostedService> hostedServices = this.serviceProvider.GetServices<IHostedService>();
        foreach (IHostedService service in hostedServices)
        {
            await service.StartAsync(cancellationToken);
        }

        try
        {
            return await this.RunReceiverAsync(cancellationToken);
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
        ApplicationMetrics? metrics = this.serviceProvider.GetService<ApplicationMetrics>();
        ApplicationInstrumentation? instrumentation = this.serviceProvider.GetService<ApplicationInstrumentation>();

        await using ReceiverHost receiverHost = ReceiverPipeline.CreateHost(
            this.aisConfig, this.timeProvider, instrumentation, metrics);

        using CompositeDisposable subscriptions = [];

        if (metrics is not null)
        {
            subscriptions.Add(receiverHost.Messages.Subscribe(msg =>
                metrics.MessagesReceived.Add(1, new KeyValuePair<string, object?>("ais.message_type", msg.MessageType))));
            subscriptions.Add(receiverHost.RawSentences.Subscribe(_ => metrics.SentencesReceived.Add(1)));
            subscriptions.Add(receiverHost.Errors.Subscribe(error =>
                metrics.ErrorsReceived.Add(1, new KeyValuePair<string, object?>("error.type", ReceiverPipeline.ClassifyError(error.Exception)))));
        }

        if (this.aisConfig.Telemetry.Verbosity == LogLevel.Warning)
        {
            subscriptions.Add(
                receiverHost.GetStreamStatistics(this.aisConfig.Telemetry.StatisticsPeriodicity)
                    .Subscribe(
                        statistics =>
                            AnsiConsole.MarkupLine($"[grey]{this.timeProvider.GetUtcNow():s}[/]: Sentences: [cyan]{statistics.Sentence}[/] | Messages: [cyan]{statistics.Message}[/] | Errors: [red]{statistics.Error}[/]"),
                        error => AnsiConsole.MarkupLine($"[red]Error in statistics stream: {Markup.Escape(error.Message)}[/]")));
        }

        if (this.aisConfig.Telemetry.Verbosity == LogLevel.Information)
        {
            subscriptions.Add(
                receiverHost.Messages.VesselNavigationWithNameStream(this.aisConfig.Telemetry.VesselInactivityTimeout).Subscribe(navigationWithName =>
                {
                    (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                    string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

                    AnsiConsole.MarkupLine($"[green][[{mmsi}: '{Markup.Escape(name.VesselName.CleanVesselName())}' ]] - [[{Markup.Escape(positionText)}]] - [[{navigation.CourseOverGround ?? 0}]][/]");
                }));
        }

        if (this.aisConfig.Telemetry.Verbosity == LogLevel.Debug)
        {
            subscriptions.Add(receiverHost.Sentences.Subscribe(AnsiConsole.WriteLine));
        }

        if (this.aisConfig.Telemetry.Verbosity == LogLevel.Trace)
        {
            subscriptions.Add(receiverHost.Messages.Subscribe(message => AnsiConsole.WriteLine(message.ToString() ?? string.Empty)));

            subscriptions.Add(
                receiverHost.Errors.Subscribe(error =>
                {
                    AnsiConsole.MarkupLine($"[red]Error received: {Markup.Escape(error.Exception.Message)}[/]");
                    AnsiConsole.MarkupLine($"[red]Bad line: {Markup.Escape(error.Line)}[/]");
                }));
        }

        StorageBatchPipeline? storagePipeline = null;
        if (this.storageConfig.EnableCapture)
        {
            ILogger<AzureAppendBlobStorageClient>? storageLogger = this.serviceProvider.GetService<ILogger<AzureAppendBlobStorageClient>>();

            // The batching, backpressure, and shutdown-flush logic is shared with the worker host via
            // ReceiverPipeline; this host supplies only its console-rendering callbacks.
            storagePipeline = ReceiverPipeline.CreateStorage(
                this.storageConfig,
                receiverHost.RawSentences,
                this.timeProvider,
                metrics,
                instrumentation,
                storageLogger,
                onPersistError: ex => AnsiConsole.MarkupLine($"[red]Storage error: {Markup.Escape(ex.Message)}[/]"),
                onSentencesDropped: total => AnsiConsole.MarkupLine($"[yellow]Storage backpressure: {total:N0} sentences dropped (batch buffer full)[/]"));
        }

        await using (storagePipeline)
        {
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
                if (storagePipeline is not null)
                {
                    await storagePipeline.FlushAsync(
                        TimeSpan.FromSeconds(30),
                        onCompleted: () => AnsiConsole.MarkupLine("[green]Storage flush completed.[/]"),
                        onTimedOut: () => AnsiConsole.MarkupLine("[yellow]Storage flush timeout.[/]"),
                        onError: ex =>
                        {
                            Activity.Current?.RecordExceptionWithStatus(ex);
                            AnsiConsole.MarkupLine($"[red]Storage flush error: {Markup.Escape(ex.Message)}[/]");
                        });
                }
            }
        }

        return 0;
    }
}