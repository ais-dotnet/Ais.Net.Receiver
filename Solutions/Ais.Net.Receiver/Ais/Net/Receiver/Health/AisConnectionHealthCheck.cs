// <copyright file="AisConnectionHealthCheck.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ais.Net.Receiver.Health;

/// <summary>
/// Health check that monitors the AIS receiver's connection status.
/// </summary>
public class AisConnectionHealthCheck : IHealthCheck
{
    private readonly IAisConnectionMonitor connectionMonitor;
    private readonly TimeSpan degradedThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="AisConnectionHealthCheck"/> class.
    /// </summary>
    /// <param name="connectionMonitor">The connection monitor.</param>
    /// <param name="degradedThreshold">Time without messages before reporting degraded status. Defaults to 5 minutes.</param>
    public AisConnectionHealthCheck(
        IAisConnectionMonitor connectionMonitor,
        TimeSpan? degradedThreshold = null)
    {
        this.connectionMonitor = connectionMonitor;
        this.degradedThreshold = degradedThreshold ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ConnectionStatus status = this.connectionMonitor.GetStatus();

        var data = new Dictionary<string, object>
        {
            { "isConnected", status.IsConnected },
            { "totalMessagesReceived", status.TotalMessagesReceived },
        };

        if (status.LastMessageTime.HasValue)
        {
            data["lastMessageTime"] = status.LastMessageTime.Value;
            data["timeSinceLastMessageSeconds"] = status.TimeSinceLastMessage.TotalSeconds;
        }

        if (!status.IsConnected)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: "Not connected to AIS data source",
                data: data));
        }

        if (status.LastMessageTime is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                description: "Connected but no messages received yet",
                data: data));
        }

        if (status.TimeSinceLastMessage > this.degradedThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                description: $"No messages received in {status.TimeSinceLastMessage.TotalMinutes:F1} minutes",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            description: $"Connected. Last message {status.TimeSinceLastMessage.TotalSeconds:F0}s ago. Total: {status.TotalMessagesReceived}",
            data: data));
    }
}
