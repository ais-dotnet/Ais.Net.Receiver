// <copyright file="ApplicationInstrumentation.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reflection;

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Centralized telemetry instrumentation for the Ais.Net.Receiver application.
/// This class provides a shared ActivitySource for distributed tracing.
/// Register as singleton in DI container.
/// </summary>
public sealed class ApplicationInstrumentation : IDisposable
{
    /// <summary>
    /// The service name used for telemetry identification.
    /// </summary>
    public const string ServiceName = "Ais.Net.Receiver";

    /// <summary>
    /// Gets the service version derived from the assembly version.
    /// </summary>
    public static readonly string ServiceVersion = typeof(ApplicationInstrumentation).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ApplicationInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "1.0.0";

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationInstrumentation"/> class.
    /// </summary>
    public ApplicationInstrumentation()
    {
        this.ActivitySource = new ActivitySource(ServiceName, ServiceVersion);
    }

    /// <summary>
    /// Gets the ActivitySource for creating distributed tracing activities.
    /// </summary>
    public ActivitySource ActivitySource { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.ActivitySource.Dispose();
    }
}
