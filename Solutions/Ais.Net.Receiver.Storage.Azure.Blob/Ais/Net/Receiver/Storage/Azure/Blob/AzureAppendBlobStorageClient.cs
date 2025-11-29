// <copyright file="AzureAppendBlobStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using global::Azure.Storage.Blobs;
using global::Azure.Storage.Blobs.Specialized;

namespace Ais.Net.Receiver.Storage.Azure.Blob;

public class AzureAppendBlobStorageClient : IStorageClient
{
    private readonly StorageConfig configuration;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private AppendBlobClient? appendBlobClient;
    private BlobContainerClient? blobContainerClient;
    private string? currentBlobPath;

    public AzureAppendBlobStorageClient(StorageConfig configuration, TimeProvider timeProvider)
    {
        this.configuration = configuration;
        this.timeProvider = timeProvider;
    }

    public async Task PersistAsync(IEnumerable<string> messages)
    {
        await this.EnsureCurrentHourBlobInitializedAsync().ConfigureAwait(false);

        using MemoryStream stream = new();
        await using (StreamWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (string message in messages)
            {
                await writer.WriteLineAsync(message).ConfigureAwait(false);
            }
        }

        stream.Position = 0;
        await this.appendBlobClient!.AppendBlockAsync(stream).ConfigureAwait(false);
    }

    public void Dispose()
    {
        this.initializationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureCurrentHourBlobInitializedAsync()
    {
        DateTimeOffset timestamp = this.timeProvider.GetUtcNow();
        string newBlobPath = $"raw/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddTHH}.nm4";

        if (this.appendBlobClient is not null && this.currentBlobPath == newBlobPath)
        {
            return;
        }

        await this.initializationLock.WaitAsync().ConfigureAwait(false);

        try
        {
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
        finally
        {
            this.initializationLock.Release();
        }
    }
}