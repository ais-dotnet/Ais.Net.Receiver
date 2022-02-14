// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class NetworkStreamNmeaReceiver : INmeaReceiver
    {
        public NetworkStreamNmeaReceiver(string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100)
        {
            this.Host = host;
            this.Port = port;
            this.RetryPeriodicity = (retryPeriodicity ?? TimeSpan.FromSeconds(1));
            this.RetryAttemptLimit = retryAttemptLimit;
        }

        public string Host { get; }

        public int Port { get; }

        public int RetryAttemptLimit { get; }

        public TimeSpan RetryPeriodicity { get; }

        //public IAsyncEnumerable<string> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        public IAsyncEnumerable<string> GetAsync(CancellationToken cancellationToken = default)
        {
            // We're letting Rx handle the retries for us. Since the rest of the code is currently written
            // to assume we return an IAsyncEnumerable (which we used to) we convert to that, but it's now
            // really all Rx. And since I think it's Rx above us too, we can probably remove IAsyncEnumerable
            // from the picture completely. This is all reactive stuff, so I don't think it really belongs.
            return this.GetObservable(cancellationToken).ToAsyncEnumerable();
        }

        public IObservable<string> GetObservable(CancellationToken cancellationToken = default)
        {
            IObservable<string> withoutRetry = Observable.Create<string>(async (obs, innerCancel) =>
            {
                using CancellationTokenSource? cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancel);
                CancellationToken mergedToken = cts.Token;

                while (!mergedToken.IsCancellationRequested)
                {
                    // Seems like we need a new one each time we try to connect, because if we reuse
                    // the previous TcpClient after a failure, it tells us it's disposed even if we
                    // didn't dispose it directly. (Perhaps disposing the NetworkStream has that effect?)
                    using TcpClient tcpClient = new();
                    await tcpClient.ConnectAsync(this.Host, this.Port, mergedToken);
                    await using NetworkStream stream = tcpClient.GetStream();
                    using StreamReader reader = new(stream);

                    int retryAttempt = 0;

                    while (tcpClient.Connected)
                    {
                        while (stream.DataAvailable && !mergedToken.IsCancellationRequested)
                        {
                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line is not null) { obs.OnNext(line); }
                            retryAttempt = 0;
                        }

                        if (mergedToken.IsCancellationRequested || retryAttempt == this.RetryAttemptLimit)
                        {
                            break;
                        }

                        await Task.Delay(this.RetryPeriodicity, mergedToken).ConfigureAwait(false);

                        retryAttempt++;
                    }

                    // Sometimes if the network connection drops, the TcpClient will just calmly set its
                    // Connected property to false and it won't throw an exception. So we need a non-exception
                    // retry loop. If we hit this point we just go round the outer try loop one more time.
                    // (It's quite likely if we hit this point that the very next thing to happen will
                    // be that the attempt to reconnect fails with an exception, but at that point the
                    // Rx-based retry will save us.
                }
            });

            // Let Rx handle the retries for us in the event of a failure that produces
            // an exception.
            return withoutRetry.Retry();
        }
    }
}