// <copyright file="StorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public AzureAppendBlobStorageClient(StorageConfig configuration)
    {
        this.configuration = configuration;
    }

    public async Task PersistAsync(IEnumerable<string> messages)
    {
        await this.InitialiseContainerAsync().ConfigureAwait(false);
        await using MemoryStream stream = new (Encoding.UTF8.GetBytes(messages.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(string.Join(",", a)), sb => sb.ToString())));
        await this.appendBlobClient!.AppendBlockAsync(stream).ConfigureAwait(false);
    }

    private async Task InitialiseContainerAsync()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        try
        {
            this.blobContainerClient = new BlobContainerClient(
                this.configuration.ConnectionString,
                this.configuration.ContainerName);

            this.appendBlobClient = new AppendBlobClient(
                this.configuration.ConnectionString,
                this.configuration.ContainerName,
                $"raw/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddTHH}.nm4");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        try
        {
            await this.blobContainerClient.CreateIfNotExistsAsync().ConfigureAwait(false);
            await this.appendBlobClient.CreateIfNotExistsAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}