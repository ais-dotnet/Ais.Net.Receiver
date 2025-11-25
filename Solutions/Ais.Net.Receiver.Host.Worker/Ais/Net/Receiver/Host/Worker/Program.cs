// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Host.Worker;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSystemd();

// Configure options with validation
builder.Services.AddOptions<AisConfig>()
    .Bind(builder.Configuration.GetSection("Ais"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<StorageConfig>()
    .Bind(builder.Configuration.GetSection("Storage"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Configure host options for graceful shutdown and exception behavior
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "Ais.Net.Receiver",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithMetrics(metrics => metrics
        .AddMeter("Ais.Net.Receiver")
        .AddRuntimeInstrumentation()
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddSource("Ais.Net.Receiver")
        .AddOtlpExporter());

IHost host = builder.Build();
await host.RunAsync();