// <copyright file="Extensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Health;
using Ais.Net.Receiver.Storage.Azure.Blob.Health;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Configuration;
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
        // Only wire up the OTLP exporters when an endpoint is configured; otherwise they default to
        // localhost:4317 and log periodic connection failures when no collector is present.
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (useOtlpExporter)
            {
                logging.AddOtlpExporter();
            }
        });

        // Stamp trace context onto every log record, so a log line can be pivoted to the trace it
        // belongs to without the call sites having to carry the ids themselves.
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.TraceState |
                ActivityTrackingOptions.TraceFlags;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: ApplicationInstrumentation.ServiceVersion,

                    // Stable per replica, honouring OTEL_SERVICE_INSTANCE_ID when the platform sets
                    // it (e.g. to a pod name). Deliberately not autoGenerateServiceInstanceId: that
                    // mints a fresh GUID per process start, so in any backend that carries resource
                    // attributes as labels every restart forks a new set of time series - breaking
                    // per-instance dashboards and growing label cardinality without bound. Under
                    // Docker and Kubernetes the container hostname is already unique per replica and
                    // stable for that replica's life, which is exactly the property wanted here.
                    serviceInstanceId: builder.Configuration["OTEL_SERVICE_INSTANCE_ID"] is { Length: > 0 } instanceId
                        ? instanceId
                        : Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                    ["service.namespace"] = "ais-net",
                })
                .AddEnvironmentVariableDetector())
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(serviceName);
                metrics.AddMeter(ApplicationMetrics.MeterName);
                foreach (string meter in additionalSources)
                {
                    metrics.AddMeter(meter);
                }

                // Attaches exemplars - sampled trace ids - to metric points, so a spike in a
                // histogram links through to example traces that produced it.
                metrics.SetExemplarFilter(ExemplarFilterType.TraceBased);

                metrics.AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter((exporterOptions, readerOptions) =>
                    {
                        readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                    });
                }
            })
            .WithTracing(tracing =>
            {
                // OTEL_TRACES_SAMPLER (with OTEL_TRACES_SAMPLER_ARG) is the standard, environment-level
                // sampling knob, and the SDK applies it only when nothing is configured in code. Any
                // programmatic SetSampler silently wins over it, so during an ingest-cost or
                // backend-overload incident an operator could set the variable, restart, see no
                // warning, and still export every span. When it is present we therefore leave
                // sampling entirely to the SDK.
                if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_TRACES_SAMPLER"]))
                {
                    // Deliberately no SetSampler call - the environment owns this decision.
                }
                else if (builder.Environment.IsDevelopment())
                {
                    // Capture everything for the Aspire dashboard.
                    tracing.SetSampler(new AlwaysOnSampler());
                }
                else
                {
                    double samplingRatio = builder.Configuration.GetValue("OpenTelemetry:TraceSamplingRatio", 1.0);

                    if (samplingRatio is < 0.0 or > 1.0)
                    {
                        throw new InvalidOperationException(
                            $"OpenTelemetry:TraceSamplingRatio must be between 0.0 and 1.0, but was {samplingRatio}");
                    }

                    // ParentBased so a sampled upstream trace stays intact end to end rather than
                    // being re-diced at this service and losing spans from the middle.
                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)));
                }

                tracing.AddSource(serviceName);
                tracing.AddSource(ApplicationInstrumentation.ServiceName);
                foreach (string source in additionalSources)
                {
                    tracing.AddSource(source);
                }

                tracing.AddHttpClientInstrumentation();

                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return builder;
    }
}
