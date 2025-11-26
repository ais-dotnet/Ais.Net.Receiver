// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Host.Console.Commands;
using Ais.Net.Receiver.Host.Console.Infrastructure;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Spectre.Console.Cli;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

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

TypeRegistrar registrar = new(builder.Services);
CommandApp<ReceiveCommand> app = new(registrar);

app.Configure(config => config.SetApplicationName("Ais.Net.Receiver.Host.Console"));

return await app.RunAsync(args);