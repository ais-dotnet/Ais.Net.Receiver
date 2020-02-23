// <copyright file="StorageClient.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Ais.Receiver
{
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class StorageClient
    {
        private CloudBlobContainer container;
        private IConfiguration configuration;

        public StorageClient(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public async Task AppendMessages(IList<string> messages)
        {
            CloudAppendBlob appendBlob;

            try
            {
                appendBlob = await GetAppendBlobAsync().ConfigureAwait(false);
            }
            catch
            {
                this.InitialiseConnection();

                appendBlob = await GetAppendBlobAsync().ConfigureAwait(false);
            }

            await appendBlob.AppendTextAsync(messages.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(String.Join(",", a)), sb => sb.ToString())).ConfigureAwait(false);
        }

        public void InitialiseConnection()
        {
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(this.configuration["connectionString"]);
            CloudBlobClient client = cloudStorageAccount.CreateCloudBlobClient();

            this.container = client.GetContainerReference(this.configuration["containerName"]);
            this.container.CreateIfNotExists();
        }

        private async Task<CloudAppendBlob> GetAppendBlobAsync()
        {
            CloudAppendBlob blob = this.container.GetAppendBlobReference($"raw/{DateTimeOffset.Now.ToString("yyyy")}/{DateTimeOffset.Now.ToString("MM")}/{DateTimeOffset.Now.ToString("dd")}/{DateTimeOffset.Now.ToString("yyyyMMddTHH")}.nm4");

            if (!blob.Exists())
            {
                await blob.CreateOrReplaceAsync().ConfigureAwait(false);
            }

            return blob;
        }
    }
}