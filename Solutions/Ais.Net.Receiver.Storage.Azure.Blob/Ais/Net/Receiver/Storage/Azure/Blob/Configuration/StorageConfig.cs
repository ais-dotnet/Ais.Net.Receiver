// <copyright file="StorageConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Configuration binding types are typically better off as null-oblivious, because the contents
// of config files are outside the compiler's control.
#nullable disable annotations

namespace Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

public class StorageConfig
{
    public string ConnectionString { get; set; }

    public string ContainerName { get; set; }

    public bool EnableCapture { get; set; }

    public int WriteBatchSize { get; set; }
}