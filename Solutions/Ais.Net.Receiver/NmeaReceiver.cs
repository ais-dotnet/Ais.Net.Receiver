// <copyright file="NmeaReceiver.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    public class NmeaReceiver
    {
        private readonly TcpClient tcpClient = new();

        public NmeaReceiver(string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100)
        {
            this.Host = host;
            this.Port = port;
            this.RetryPeriodicity = (retryPeriodicity ??= TimeSpan.FromSeconds(1));
            this.RetryAttemptLimit = retryAttemptLimit;
        }

        public bool Connected => this.tcpClient.Connected;

        public string Host { get; }

        public int Port { get; }

        public int RetryAttemptLimit { get; }

        public TimeSpan RetryPeriodicity { get;  }

        public async IAsyncEnumerable<string> GetAsync()
        {
            await this.tcpClient.ConnectAsync(this.Host, this.Port);
            await using NetworkStream stream = this.tcpClient.GetStream();
            using StreamReader reader = new StreamReader(stream);

            int retryAttempt = 0;

            while (this.tcpClient.Connected)
            {
                while (stream.DataAvailable)
                {
                    yield return await reader.ReadLineAsync().ConfigureAwait(false);
                    retryAttempt = 0;
                }

                if (retryAttempt == this.RetryAttemptLimit)
                {
                    break;
                }

                await Task.Delay(this.RetryPeriodicity).ConfigureAwait(false);

                retryAttempt++;
            }
        }
    }
}