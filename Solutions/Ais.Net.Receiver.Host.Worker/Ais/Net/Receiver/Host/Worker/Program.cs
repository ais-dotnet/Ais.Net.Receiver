// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ais.Net.Receiver.Host.Worker;

public static class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        builder.Services.AddSystemd();
        builder.Services.AddHostedService<Worker>();

        builder.Configuration.AddJsonFile("settings.json", true, true);
        builder.Configuration.AddJsonFile("settings.local.json", true, true);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Ais.Net.Receiver"))
            .WithMetrics(metrics => metrics
                .AddMeter("Ais.Net.Receiver")
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing => tracing
                .AddSource("Ais.Net.Receiver")
                .AddOtlpExporter());

        IHost host = builder.Build();
        host.Run();
    }
}