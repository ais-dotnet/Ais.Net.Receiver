// <copyright file="NmeaReceiver.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Ais.Receiver
{
    using System;
    using System.IO;
    using System.Reactive.Subjects;
    using System.Threading.Tasks;

    public class NmeaReceiver
    {
        private System.Net.Sockets.TcpClient tcpClient;
        private Subject<string> items;

        public NmeaReceiver(string host, int port)
        {
            Host = host;
            Port = port;

            this.tcpClient = new System.Net.Sockets.TcpClient();
        }

        public bool Connected
        {
            get { return this.tcpClient.Connected; }
        }

        public string Host { get; }

        public int Port { get; }

        public IObservable<string> Items
        {
            get { return this.items ?? (this.items = new Subject<string>()); }
        }

        protected Subject<string> Subject
        {
            get { return this.items; }
        }

        public async Task InitaliseAsync()
        {
            this.tcpClient = new System.Net.Sockets.TcpClient();
            await this.tcpClient.ConnectAsync(this.Host, this.Port);
        }

        public async Task RecieveAsync()
        {
            int retryLoop = 0;
            var stream = this.tcpClient.GetStream();

            using (var reader = new StreamReader(stream))
            {
                while (this.tcpClient.Connected)
                {
                    while (stream.DataAvailable)
                    {
                        this.Subject.OnNext(await reader.ReadLineAsync().ConfigureAwait(false));
                        retryLoop = 0;
                    }

                    if (retryLoop == 100)
                    {
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

                    retryLoop++;
                }
            }
        }
    }
}