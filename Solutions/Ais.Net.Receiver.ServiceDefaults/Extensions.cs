// <copyright file="Extensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Health;
using Ais.Net.Receiver.Storage.Azure.Blob.Health;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring service defaults including OpenTelemetry and service discovery.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds default service configuration including OpenTelemetry, service discovery, and HTTP client resilience.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="serviceName">The primary service name for telemetry.</param>
    /// <param name="additionalSources">Additional activity sources and meters to register.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddServiceDefaults(
        this IHostApplicationBuilder builder,
        string serviceName,
        params string[] additionalSources)
    {
        builder.ConfigureOpenTelemetry(serviceName, additionalSources);
        builder.AddAisInstrumentation();
        builder.AddAisHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds AIS-specific instrumentation services.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddAisInstrumentation(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ApplicationInstrumentation>();
        builder.Services.AddSingleton<ApplicationMetrics>();
        builder.Services.AddSingleton<IAisConnectionMonitor, AisConnectionMonitor>();

        return builder;
    }

    /// <summary>
    /// Adds AIS-specific health checks.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddAisHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<AisConnectionHealthCheck>(
                "ais-connection",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["live", "ready"])
            .AddCheck<StorageHealthCheck>(
                "storage",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry with metrics and tracing for the specified service.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="serviceName">The primary service name.</param>
    /// <param name="additionalSources">Additional activity sources and meters to register.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        params string[] additionalSources)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.AddOtlpExporter();
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: ApplicationInstrumentation.ServiceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                    ["service.namespace"] = "ais-net",
                }))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(serviceName);
                metrics.AddMeter(ApplicationMetrics.MeterName);
                foreach (string meter in additionalSources)
                {
                    metrics.AddMeter(meter);
                }

                metrics.AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter((exporterOptions, readerOptions) =>
                    {
                        readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                    });
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(serviceName);
                tracing.AddSource(ApplicationInstrumentation.ServiceName);
                foreach (string source in additionalSources)
                {
                    tracing.AddSource(source);
                }

                tracing.AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            });

        return builder;
    }
}
