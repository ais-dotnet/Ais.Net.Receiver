// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

using System;
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

    public bool Connected => (this.tcpClient?.Connected ?? false) && (this.stream?.Socket.Connected ?? false);

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        this.tcpClient = new TcpClient();

        try
        {
            await this.tcpClient.ConnectAsync(host, port, cancellationToken);
            this.stream = this.tcpClient.GetStream();
            this.reader = new StreamReader(this.stream);
        }
        catch (Exception)
        {
            // If connection fails, clean up resources
            await this.DisposeAsync();
            throw;
        }
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return this.reader is not null
            ? await this.reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (this.reader is not null)
        {
            try { this.reader.Dispose(); } catch { /* Ignore any errors during cleanup */ }
            this.reader = null;
        }

        if (this.stream is not null)
        {
            try { await this.stream.DisposeAsync(); } catch { /* Ignore any errors during cleanup */ }
            this.stream = null;
        }

        if (this.tcpClient is not null)
        {
            try { this.tcpClient.Dispose(); } catch { /* Ignore any errors during cleanup */ }
            this.tcpClient = null;
        }
    }
}