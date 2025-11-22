// <copyright file="StorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using global::Azure.Storage.Blobs;
using global::Azure.Storage.Blobs.Specialized;

namespace Ais.Net.Receiver.Storage.Azure.Blob;

public class AzureAppendBlobStorageClient : IStorageClient
{
    private readonly StorageConfig configuration;
    private AppendBlobClient? appendBlobClient;
    private BlobContainerClient? blobContainerClient;
    private string? currentBlobPath;

    public AzureAppendBlobStorageClient(StorageConfig configuration)
    {
        this.configuration = configuration;
    }

    public async Task PersistAsync(IEnumerable<string> messages)
    {
        await this.EnsureClientInitializedAsync().ConfigureAwait(false);

        using MemoryStream stream = new();
        using (StreamWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (string message in messages)
            {
                await writer.WriteLineAsync(message).ConfigureAwait(false);
            }
        }

        stream.Position = 0;
        await this.appendBlobClient!.AppendBlockAsync(stream).ConfigureAwait(false);
    }

    private async Task EnsureClientInitializedAsync()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        string newBlobPath = $"raw/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddTHH}.nm4";

        if (this.appendBlobClient is not null && this.currentBlobPath == newBlobPath)
        {
            return;
        }

        this.currentBlobPath = newBlobPath;

        this.blobContainerClient ??= new BlobContainerClient(
            this.configuration.ConnectionString,
            this.configuration.ContainerName);

        this.appendBlobClient = this.blobContainerClient.GetAppendBlobClient(newBlobPath);

        await this.blobContainerClient.CreateIfNotExistsAsync().ConfigureAwait(false);
        await this.appendBlobClient.CreateIfNotExistsAsync().ConfigureAwait(false);
    }
}