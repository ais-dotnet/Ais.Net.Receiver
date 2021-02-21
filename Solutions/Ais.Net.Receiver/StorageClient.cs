// <copyright file="StorageClient.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Ais.Receiver
{
    using Azure.Storage.Blobs.Specialized;

    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class StorageClient
    {
        private AppendBlobClient container;
        private IConfiguration configuration;
        private Stream stream;

        public StorageClient(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public async Task AppendMessages(IEnumerable<string> messages)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(messages.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(String.Join(",", a)), sb => sb.ToString())))) 
            {
                await this.container.AppendBlockAsync(stream).ConfigureAwait(false);
            }
        }

        public async Task InitialiseConnectionAsync()
        {
            string blobName = $"raw/{DateTimeOffset.Now.ToString("yyyy")}/{DateTimeOffset.Now.ToString("MM")}/{DateTimeOffset.Now.ToString("dd")}/{DateTimeOffset.Now.ToString("yyyyMMddTHH")}.nm4";
            this.container = new AppendBlobClient(this.configuration["connectionString"], this.configuration["containerName"], blobName);
            
            await this.container.CreateIfNotExistsAsync().ConfigureAwait(false);
        }
    }
}