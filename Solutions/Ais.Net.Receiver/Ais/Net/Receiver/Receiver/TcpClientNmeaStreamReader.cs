// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Receiver;

public class TcpClientNmeaStreamReader : INmeaStreamReader
{
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private StreamReader? reader;

    public bool Connected => this.tcpClient?.Connected == true && this.stream is not null;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await this.DisposeAsync().ConfigureAwait(false);

        this.tcpClient = new TcpClient
        {
            ReceiveBufferSize = 65_536, // 64KB buffer for bursty traffic
            NoDelay = true // Disable Nagle's algorithm for lower latency
        };

        try
        {
            await this.tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            this.stream = this.tcpClient.GetStream();
            this.reader = new StreamReader(this.stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 65536, leaveOpen: true);
        }
        catch (Exception)
        {
            // If connection fails, clean up resources
            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
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
            try { await this.stream.DisposeAsync().ConfigureAwait(false); } catch { /* Ignore any errors during cleanup */ }
            this.stream = null;
        }

        if (this.tcpClient is not null)
        {
            try { this.tcpClient.Dispose(); } catch { /* Ignore any errors during cleanup */ }
            this.tcpClient = null;
        }

        GC.SuppressFinalize(this);
    }
}