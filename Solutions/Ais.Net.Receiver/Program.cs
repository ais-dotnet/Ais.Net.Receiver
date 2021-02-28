// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("local.settings.json", true, true)
                            .Build();

            IStorageClient storageClient = new StorageClient(config);

            var receiverHost = new ReceiverHost(config, storageClient);
            await receiverHost.StartAsync();
        }
    }
}