// <copyright file="AisConnectionHealthCheck.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Health;

/// <summary>
/// Health check that monitors the AIS receiver's connection status.
/// </summary>
public class AisConnectionHealthCheck : IHealthCheck
{
    private const string CheckName = "ais-connection";
    private readonly IAisConnectionMonitor connectionMonitor;
    private readonly TimeSpan degradedThreshold;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AisConnectionHealthCheck"/> class.
    /// </summary>
    /// <param name="connectionMonitor">The connection monitor.</param>
    /// <param name="degradedThreshold">Time without messages before reporting degraded status. Defaults to 5 minutes.</param>
    /// <param name="logger">The logger.</param>
    public AisConnectionHealthCheck(
        IAisConnectionMonitor connectionMonitor,
        TimeSpan? degradedThreshold = null,
        ILogger<AisConnectionHealthCheck>? logger = null)
    {
        this.connectionMonitor = connectionMonitor;
        this.degradedThreshold = degradedThreshold ?? TimeSpan.FromMinutes(5);
        this.logger = logger ?? NullLogger<AisConnectionHealthCheck>.Instance;
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
            string reason = "Not connected to AIS data source";
            this.logger.HealthCheckUnhealthy(CheckName, reason);
            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: reason,
                data: data));
        }

        if (status.LastMessageTime is null)
        {
            string reason = "Connected but no messages received yet";
            this.logger.HealthCheckDegraded(CheckName, reason);
            return Task.FromResult(HealthCheckResult.Degraded(
                description: reason,
                data: data));
        }

        if (status.TimeSinceLastMessage > this.degradedThreshold)
        {
            string reason = $"No messages received in {status.TimeSinceLastMessage.TotalMinutes:F1} minutes";
            this.logger.ConnectionStale(status.TimeSinceLastMessage.TotalSeconds);
            this.logger.HealthCheckDegraded(CheckName, reason);
            return Task.FromResult(HealthCheckResult.Degraded(
                description: reason,
                data: data));
        }

        string description = $"Connected. Last message {status.TimeSinceLastMessage.TotalSeconds:F0}s ago. Total: {status.TotalMessagesReceived}";
        this.logger.HealthCheckCompleted(CheckName, "Healthy");
        return Task.FromResult(HealthCheckResult.Healthy(
            description: description,
            data: data));
    }
}
