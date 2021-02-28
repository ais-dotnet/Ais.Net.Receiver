// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;
    using System.Linq;
    using System.Reactive.Linq;

    public class ReceiverHost
    {
        private readonly IConfiguration configuration;
        private readonly IStorageClient storageClient;
        private NmeaReceiver receiver;

        public ReceiverHost(IConfiguration configuration, IStorageClient storageClient)
        {
            this.configuration = configuration;
            this.storageClient = storageClient;
        }

        public async Task StartAsync()
        {
            this.receiver = new NmeaReceiver(
                this.configuration[ConfigurationKeys.Host],
                int.Parse(this.configuration[ConfigurationKeys.HostPort]),
                TimeSpan.Parse(this.configuration[ConfigurationKeys.ConnectionRetryPeriodicity]),
                int.Parse(this.configuration[ConfigurationKeys.ConnectionRetryAttempts]));

            IObservable<string> messages = this.GetAsync().ToObservable();
            await messages.Buffer(int.Parse(this.configuration[ConfigurationKeys.WriteBatchSize])).Select(this.storageClient.PersistAsync);
        }

        public async IAsyncEnumerable<string> GetAsync()
        {
            while (!this.receiver.Connected)
            {
                await foreach (var message in this.receiver.GetAsync())
                {
                    if (message.IsMissingNmeaBlockTags())
                    {
                        yield return message.PrependNmeaBlockTags();
                    }
                    else
                    {
                        yield return message;
                    }
                }
            }
        }

        private static class ConfigurationKeys
        {
            public const string Host = "host";
            public const string HostPort = "host-port";
            public const string ConnectionRetryAttempts = "connection-retry-attempts";
            public const string ConnectionRetryPeriodicity = "connection-retry-periodicity";
            public const string WriteBatchSize = "write-batch-size";
        }
    }
}