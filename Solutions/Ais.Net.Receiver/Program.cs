// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Ais.Receiver
{
    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.Reactive.Linq;
    using System.Threading.Tasks;

    public static class Program
    {
        private static StorageClient storageClient;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("local.settings.json", true, true)
                            .Build();

            storageClient = new StorageClient(config);
            storageClient.InitialiseConnection();

            var receiver = new NmeaReceiver("153.44.253.27", 5631);
            receiver.Items.Buffer(100).Subscribe(OnMessageReceived, OnError);

            while (!receiver.Connected)
            {
                await receiver.InitaliseAsync().ConfigureAwait(false);
                await receiver.RecieveAsync().ConfigureAwait(false);
            }
        }

        private static void OnError(Exception exception)
        {
            Console.WriteLine(exception.Message);
        }

        private static void OnMessageReceived(IList<string> messages)
        {
            foreach (var message in messages)
            {
                Console.WriteLine($"{message}");
            }
                       
            storageClient.AppendMessages(messages);
        }

        
    }
}