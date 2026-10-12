// <copyright file="StorageConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

public class StorageConfig
{
    // Required only when EnableCapture is true - enforced by StorageConfigValidator - so the receiver
    // can run for live display with no storage configured.
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public bool EnableCapture { get; set; }

    [Range(1, 10000)]
    public int WriteBatchSize { get; set; } = 500;

    [Range(1, 300)]
    public int BatchTimeoutSeconds { get; set; } = 10;

    [Range(1, 100000)]
    public int BoundedCapacity { get; set; } = 10000;

    /// <summary>
    /// Parallelism for storage writes. Defaults to 1: an hourly append blob is written sequentially,
    /// so concurrent writers would reorder blocks and race the hour-boundary blob swap. Only raise
    /// this if you understand those trade-offs.
    /// </summary>
    [Range(1, 8)]
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Number of attempts to persist a batch before it is abandoned or dead-lettered (this is on top
    /// of the Azure SDK's own transient-fault retries, to survive longer outages).
    /// </summary>
    [Range(1, 20)]
    public int WriteRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Optional local directory. When set, a batch that still fails after <see cref="WriteRetryAttempts"/>
    /// is written here (newline-separated NMEA) for later replay instead of being dropped.
    /// </summary>
    public string? DeadLetterPath { get; set; }

    /// <summary>
    /// How often, in seconds, to sweep <see cref="DeadLetterPath"/> and replay dead-lettered batches
    /// back to storage once the backend recovers. Only has an effect when <see cref="DeadLetterPath"/>
    /// is set.
    /// </summary>
    [Range(1, 3600)]
    public int DeadLetterReplayIntervalSeconds { get; set; } = 60;
}