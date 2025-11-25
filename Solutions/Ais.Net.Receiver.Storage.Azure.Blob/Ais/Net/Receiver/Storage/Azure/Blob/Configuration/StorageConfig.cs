// <copyright file="StorageConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

public class StorageConfig
{
    [Required]
    public required string ConnectionString { get; set; }

    [Required]
    public required string ContainerName { get; set; }

    public bool EnableCapture { get; set; }

    [Range(1, 10000)]
    public int WriteBatchSize { get; set; } = 500;

    [Range(1, 300)]
    public int BatchTimeoutSeconds { get; set; } = 10;

    [Range(1, 100000)]
    public int BoundedCapacity { get; set; } = 10000;

    [Range(1, 8)]
    public int MaxDegreeOfParallelism { get; set; } = 2;
}