// <copyright file="Extensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

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
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(Extensions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    serviceInstanceId: Environment.MachineName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(serviceName);
                foreach (string meter in additionalSources)
                {
                    metrics.AddMeter(meter);
                }

                metrics.AddRuntimeInstrumentation()
                    .AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(serviceName);
                foreach (string source in additionalSources)
                {
                    tracing.AddSource(source);
                }

                tracing.AddOtlpExporter();
            });

        return builder;
    }
}
