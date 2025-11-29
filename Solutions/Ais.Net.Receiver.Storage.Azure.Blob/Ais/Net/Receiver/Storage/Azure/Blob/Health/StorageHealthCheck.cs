// <copyright file="StorageHealthCheck.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Azure.Storage.Blobs;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Storage.Azure.Blob.Health;

/// <summary>
/// Health check that verifies Azure Blob storage connectivity.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly StorageConfig config;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageHealthCheck"/> class.
    /// </summary>
    /// <param name="config">The storage configuration.</param>
    public StorageHealthCheck(IOptions<StorageConfig> config)
    {
        this.config = config.Value;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!this.config.EnableCapture)
        {
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
                return HealthCheckResult.Degraded(
                    description: "Container does not exist, will be created on first write",
                    data: data);
            }

            data["containerExists"] = true;
            return HealthCheckResult.Healthy(
                description: "Storage is accessible",
                data: data);
        }
        catch (Exception ex)
        {
            data["error"] = ex.Message;
            return HealthCheckResult.Unhealthy(
                description: "Cannot access storage",
                exception: ex,
                data: data);
        }
    }
}