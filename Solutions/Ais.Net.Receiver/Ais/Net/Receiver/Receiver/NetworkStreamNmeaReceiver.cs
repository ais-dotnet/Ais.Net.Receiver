// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver
{
    private readonly INmeaStreamReader nmeaStreamReader;

    public NetworkStreamNmeaReceiver(string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100)
        : this(new TcpClientNmeaStreamReader(), host, port, retryPeriodicity, retryAttemptLimit)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100)
    {
        this.Host = host;
        this.Port = port;
        this.RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1);
        this.RetryAttemptLimit = retryAttemptLimit;
        this.nmeaStreamReader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public string Host { get; }
    
    public int Port { get; }
    
    public int RetryAttemptLimit { get; }
    
    public TimeSpan RetryPeriodicity { get; }

    // We still provide the IAsyncEnumerable API for backwards compatibility.
    public IAsyncEnumerable<string> GetAsync(CancellationToken cancellationToken = default)
    {
        return this.GetObservable(cancellationToken).ToAsyncEnumerable();
    }

    public IObservable<string> GetObservable(CancellationToken cancellationToken = default)
    {
        IObservable<string> withoutRetry = Observable.Create<string>(async (obs, innerCancel) =>
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancel);
            CancellationToken mergedToken = cts.Token;

            while (!mergedToken.IsCancellationRequested)
            {
                await this.nmeaStreamReader.ConnectAsync(this.Host, this.Port, mergedToken);

                int retryAttempt = 0;

                try
                {
                    while (this.nmeaStreamReader.Connected)
                    {
                        while (this.nmeaStreamReader.DataAvailable && !mergedToken.IsCancellationRequested)
                        {
                            string? line = await this.nmeaStreamReader.ReadLineAsync(mergedToken).ConfigureAwait(false);
                            if (line is not null)
                            {
                                obs.OnNext(line);
                            }
                            retryAttempt = 0;
                        }

                        if (mergedToken.IsCancellationRequested || retryAttempt == this.RetryAttemptLimit)
                        {
                            break;
                        }

                        await Task.Delay(this.RetryPeriodicity, mergedToken).ConfigureAwait(false);
                        retryAttempt++;
                    }
                }
                finally
                {
                    await this.nmeaStreamReader.DisposeAsync();
                }
            }
        });

        return withoutRetry.Retry();
    }
}