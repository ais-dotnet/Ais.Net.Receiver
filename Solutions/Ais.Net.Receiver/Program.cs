// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using Microsoft.Extensions.Configuration;

    using System.Threading.Tasks;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("local.settings.json", true, true)
                            .Build();

            var aisConfig = config.GetSection("Ais").Get<AisConfig>();
            var storageConfig = config.GetSection("Storage").Get<StorageConfig>();

            IStorageClient storageClient = new StorageClient(storageConfig);

            var receiverHost = new ReceiverHost(aisConfig, storageClient);
            await receiverHost.StartAsync();
        }
    }
}