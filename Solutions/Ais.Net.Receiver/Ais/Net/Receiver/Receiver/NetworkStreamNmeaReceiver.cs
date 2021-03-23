// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    public class NetworkStreamNmeaReceiver : INmeaReceiver
    {
        private readonly TcpClient tcpClient = new();

        public NetworkStreamNmeaReceiver(string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100)
        {
            this.Host = host;
            this.Port = port;
            this.RetryPeriodicity = (retryPeriodicity ?? TimeSpan.FromSeconds(1));
            this.RetryAttemptLimit = retryAttemptLimit;
        }

        public bool Connected => this.tcpClient.Connected;

        public string Host { get; }

        public int Port { get; }

        public int RetryAttemptLimit { get; }

        public TimeSpan RetryPeriodicity { get; }

        public async IAsyncEnumerable<string> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await this.tcpClient.ConnectAsync(this.Host, this.Port, cancellationToken);
            await using NetworkStream stream = this.tcpClient.GetStream();
            using StreamReader reader = new(stream);

            int retryAttempt = 0;

            while (this.tcpClient.Connected)
            {
                while (stream.DataAvailable && !cancellationToken.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is not null) { yield return line; }
                    retryAttempt = 0;
                }

                if (cancellationToken.IsCancellationRequested || retryAttempt == this.RetryAttemptLimit)
                {
                    break;
                }

                await Task.Delay(this.RetryPeriodicity, cancellationToken).ConfigureAwait(false);

                retryAttempt++;
            }
        }
    }
}