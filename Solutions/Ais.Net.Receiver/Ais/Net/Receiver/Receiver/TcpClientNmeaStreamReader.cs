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

    public bool DataAvailable
    {
        get { return this.stream?.DataAvailable ?? false; }
    }

    public bool Connected
    {
        get { return this.tcpClient?.Connected ?? (this.stream?.Socket.Connected) ?? false; }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        this.tcpClient = new TcpClient();

        try
        {
            await this.tcpClient.ConnectAsync(host, port, cancellationToken);

            // Configure socket options for better detection of broken connections
            if (this.tcpClient.Client is { } socket)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
            }

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
        if (this.reader != null)
        {
            try { this.reader.Dispose(); } catch { /* Ignore any errors during cleanup */ }
            this.reader = null;
        }

        if (this.stream != null)
        {
            try { await this.stream.DisposeAsync(); } catch { /* Ignore any errors during cleanup */ }
            this.stream = null;
        }

        if (this.tcpClient != null)
        {
            try { this.tcpClient.Dispose(); } catch { /* Ignore any errors during cleanup */ }
            this.tcpClient = null;
        }
    }
}