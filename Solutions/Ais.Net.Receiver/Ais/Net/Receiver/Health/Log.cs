// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Health;

/// <summary>
/// Source-generated logging methods for health check components.
/// </summary>
internal static partial class Log
{
    // Health check events (5000-5009)
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Debug,
        Message = "Health check '{CheckName}' completed with status {Status}")]
    public static partial void HealthCheckCompleted(this ILogger logger, string checkName, string status);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Warning,
        Message = "Health check '{CheckName}' degraded: {Reason}")]
    public static partial void HealthCheckDegraded(this ILogger logger, string checkName, string reason);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Warning,
        Message = "Health check '{CheckName}' unhealthy: {Reason}")]
    public static partial void HealthCheckUnhealthy(this ILogger logger, string checkName, string reason);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Warning,
        Message = "No AIS messages received for {Seconds:F0}s, connection may be stale")]
    public static partial void ConnectionStale(this ILogger logger, double seconds);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Error,
        Message = "Health check '{CheckName}' failed with error")]
    public static partial void HealthCheckError(this ILogger logger, Exception exception, string checkName);
}
