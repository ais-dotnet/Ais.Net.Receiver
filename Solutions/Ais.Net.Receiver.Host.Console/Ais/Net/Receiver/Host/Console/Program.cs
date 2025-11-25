// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Spectre.Console;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure options with validation
builder.Services.AddOptions<AisConfig>()
    .Bind(builder.Configuration.GetSection("Ais"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<StorageConfig>()
    .Bind(builder.Configuration.GetSection("Storage"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "Ais.Net.Receiver.Console",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithMetrics(metrics => metrics
        .AddMeter("Ais.Net.Receiver.Console")
        .AddRuntimeInstrumentation()
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddSource("Ais.Net.Receiver.Console")
        .AddSource("Ais.Net.Receiver")
        .AddOtlpExporter());

using IHost host = builder.Build();

// Start the host to enable hosted services and lifetime management
await host.StartAsync();

IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
AisConfig aisConfig = host.Services.GetRequiredService<IOptions<AisConfig>>().Value;
StorageConfig storageConfig = host.Services.GetRequiredService<IOptions<StorageConfig>>().Value;

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

using CompositeDisposable subscriptions = [];
BatchBlock<string>? batchBlock = null;
ActionBlock<IEnumerable<string>>? actionBlock = null;

if (aisConfig.LoggerVerbosity == LoggerVerbosity.Minimal)
{
    subscriptions.Add(
        receiverHost.GetStreamStatistics(aisConfig.StatisticsPeriodicity)
            .Subscribe(
                statistics =>
                    AnsiConsole.MarkupLine($"[grey]{DateTime.UtcNow.ToUniversalTime():s}[/]: Sentences: [cyan]{statistics.Sentence}[/] | Messages: [cyan]{statistics.Message}[/] | Errors: [red]{statistics.Error}[/]"),
                error => AnsiConsole.MarkupLine($"[red]Error in statistics stream: {Markup.Escape(error.Message)}[/]")));
}

if (aisConfig.LoggerVerbosity == LoggerVerbosity.Normal)
{
    subscriptions.Add(
        receiverHost.Messages.VesselNavigationWithNameStream().Subscribe(navigationWithName =>
        {
            (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
            string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";

            AnsiConsole.MarkupLine($"[green][[{mmsi}: '{Markup.Escape(name.VesselName.CleanVesselName())}' ]] - [[{Markup.Escape(positionText)}]] - [[{navigation.CourseOverGround ?? 0}]][/]");
        }));
}

if (aisConfig.LoggerVerbosity == LoggerVerbosity.Detailed)
{
    // Write out the messages as they are received over the wire.
    subscriptions.Add(receiverHost.Sentences.Subscribe(sentence => AnsiConsole.WriteLine(sentence)));
}

if (aisConfig.LoggerVerbosity == LoggerVerbosity.Diagnostic)
{
    subscriptions.Add(receiverHost.Messages.Subscribe(message => AnsiConsole.WriteLine(message.ToString() ?? string.Empty)));

    // Write out errors in the console
    subscriptions.Add(
        receiverHost.Errors.Subscribe(error =>
        {
            AnsiConsole.MarkupLine($"[red]Error received: {Markup.Escape(error.Exception.Message)}[/]");
            AnsiConsole.MarkupLine($"[red]Bad line: {Markup.Escape(error.Line)}[/]");
        }));
}

if (storageConfig.EnableCapture)
{
    IStorageClient storageClient = new AzureAppendBlobStorageClient(storageConfig);
    batchBlock = new BatchBlock<string>(storageConfig.WriteBatchSize);
    actionBlock = new ActionBlock<IEnumerable<string>>(storageClient.PersistAsync);
    batchBlock.LinkTo(actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

    // Persist the messages as they are received over the wire.
    subscriptions.Add(receiverHost.Sentences.Subscribe(batchBlock.AsObserver()));
}

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    AnsiConsole.MarkupLine("[yellow]Stopping...[/]");
    lifetime.StopApplication();
};

try
{
    // Use the host's ApplicationStopping token for cancellation
    await receiverHost.StartAsync(lifetime.ApplicationStopping);
}
catch (OperationCanceledException)
{
    // Expected on cancellation
}
finally
{
    // Complete the dataflow pipeline and wait for it to finish
    if (batchBlock is not null && actionBlock is not null)
    {
        batchBlock.Complete();
        try
        {
            await actionBlock.Completion;
            AnsiConsole.MarkupLine("[green]Storage flush completed.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Storage flush error: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

await host.StopAsync();