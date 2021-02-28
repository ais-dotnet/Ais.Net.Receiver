// <copyright file="StorageClient.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Specialized;

    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class StorageClient : IStorageClient
    {
        private readonly IConfiguration configuration;
        private AppendBlobClient appendBlobClient;
        private BlobContainerClient blobContainerClient;

        public StorageClient(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public async Task PersistAsync(IEnumerable<string> messages)
        {
            await this.InitialiseContainerAsync().ConfigureAwait(false);
            await using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(messages.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(string.Join(",", a)), sb => sb.ToString())));
            await this.appendBlobClient.AppendBlockAsync(stream).ConfigureAwait(false);
        }

        private async Task InitialiseContainerAsync()
        {
            try
            {
                this.blobContainerClient = new BlobContainerClient(
                    this.configuration[ConfigurationKeys.ConnectionString],
                    this.configuration[ConfigurationKeys.ContainerName]);
                this.appendBlobClient = new AppendBlobClient(
                    this.configuration[ConfigurationKeys.ConnectionString],
                    this.configuration[ConfigurationKeys.ContainerName],
                    $"raw/{DateTimeOffset.Now:yyyy}/{DateTimeOffset.Now:MM}/{DateTimeOffset.Now:dd}/{DateTimeOffset.Now:yyyyMMddTHH}.nm4");
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

        private static class ConfigurationKeys
        {
            public const string ConnectionString = "connectionString";
            public const string ContainerName = "containerName";
        }
    }
}