// <copyright file="StorageConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

public class StorageConfig
{
    public required string ConnectionString { get; set; }

    public required string ContainerName { get; set; }

    public bool EnableCapture { get; set; }

    public int WriteBatchSize { get; set; }
}