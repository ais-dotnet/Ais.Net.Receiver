// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public class TcpClientNmeaStreamReader : INmeaStreamReader
{
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private StreamReader? reader;

    public bool DataAvailable => this.stream?.DataAvailable ?? false;
    
    public bool Connected => this.tcpClient?.Connected ?? false;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        this.tcpClient = new TcpClient();
        await this.tcpClient.ConnectAsync(host, port, cancellationToken);
        this.stream = this.tcpClient.GetStream();
        this.reader = new StreamReader(this.stream);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return this.reader is not null 
            ? await this.reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        this.reader?.Dispose();

        if (this.stream is not null)
        {
            await this.stream.DisposeAsync();
        }

        this.tcpClient?.Dispose();
    }
}