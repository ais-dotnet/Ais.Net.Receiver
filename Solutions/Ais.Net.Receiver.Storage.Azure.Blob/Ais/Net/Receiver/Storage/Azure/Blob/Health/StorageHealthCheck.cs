// <copyright file="StorageHealthCheck.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Azure.Storage.Blobs;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Storage.Azure.Blob.Health;

/// <summary>
/// Health check that verifies Azure Blob storage connectivity.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly StorageConfig config;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageHealthCheck"/> class.
    /// </summary>
    /// <param name="config">The storage configuration.</param>
    /// <param name="logger">The logger.</param>
    public StorageHealthCheck(IOptions<StorageConfig> config, ILogger<StorageHealthCheck>? logger = null)
    {
        this.config = config.Value;
        this.logger = logger ?? NullLogger<StorageHealthCheck>.Instance;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!this.config.EnableCapture)
        {
            this.logger.StorageCaptureDisabled();
            return HealthCheckResult.Healthy(
                description: "Storage capture is disabled",
                data: new Dictionary<string, object> { { "enabled", false } });
        }

        var data = new Dictionary<string, object>
        {
            { "enabled", true },
            { "containerName", this.config.ContainerName },
        };

        try
        {
            BlobContainerClient client = new(
                this.config.ConnectionString,
                this.config.ContainerName);

            bool exists = await client.ExistsAsync(cancellationToken).ConfigureAwait(false);

            if (!exists)
            {
                data["containerExists"] = false;
                string reason = "Container does not exist, will be created on first write";
                this.logger.StorageHealthCheckDegraded(reason);
                return HealthCheckResult.Degraded(
                    description: reason,
                    data: data);
            }

            data["containerExists"] = true;
            this.logger.StorageHealthCheckCompleted("Healthy");
            return HealthCheckResult.Healthy(
                description: "Storage is accessible",
                data: data);
        }
        catch (Exception ex)
        {
            data["error"] = ex.Message;
            this.logger.StorageHealthCheckFailed(ex);
            return HealthCheckResult.Unhealthy(
                description: "Cannot access storage",
                exception: ex,
                data: data);
        }
    }
}