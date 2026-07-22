// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

public class TcpClientNmeaStreamReader : INmeaStreamReader
{
    private readonly ILogger logger;
    private byte[] lineBuffer = new byte[512];
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private PipeReader? reader;
    private string? currentHost;
    private int currentPort;

    public TcpClientNmeaStreamReader(ILogger<TcpClientNmeaStreamReader>? logger = null)
    {
        this.logger = logger ?? NullLogger<TcpClientNmeaStreamReader>.Instance;
    }

    public bool Connected => this.tcpClient?.Connected == true && this.stream is not null;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await this.DisposeAsync().ConfigureAwait(false);

        this.currentHost = host;
        this.currentPort = port;

        this.tcpClient = new TcpClient
        {
            ReceiveBufferSize = 65_536, // 64KB buffer for bursty traffic
            NoDelay = true // Disable Nagle's algorithm for lower latency
        };

        try
        {
            this.logger.TcpConnecting(host, port);
            await this.tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            this.stream = this.tcpClient.GetStream();
            this.reader = PipeReader.Create(this.stream);
            this.logger.TcpConnected(host, port);
        }
        catch (Exception ex)
        {
            // If connection fails, clean up resources
            this.logger.TcpConnectionFailed(ex, host, port);
            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (this.reader is null)
        {
            return null;
        }

        while (true)
        {
            ReadResult result = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (position != null)
            {
                // Found a line. Copy it into a reusable buffer (grown as needed) rather than
                // allocating a fresh array per line. The returned memory is only valid until the
                // next ReadLineAsync/DisposeAsync call, as documented on INmeaStreamReader.
                ReadOnlySequence<byte> line = buffer.Slice(0, position.Value);
                int length = (int)line.Length;

                if (length > this.lineBuffer.Length)
                {
                    this.lineBuffer = new byte[Math.Max(length, this.lineBuffer.Length * 2)];
                }

                line.CopyTo(this.lineBuffer);

                // Advance reader past the newline
                this.reader.AdvanceTo(buffer.GetPosition(1, position.Value));

                // Trim \r if present
                if (length > 0 && this.lineBuffer[length - 1] == (byte)'\r')
                {
                    length--;
                }

                return this.lineBuffer.AsMemory(0, length);
            }

            this.reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        bool wasConnected = this.tcpClient?.Connected == true;

        if (this.reader is not null)
        {
            try { await this.reader.CompleteAsync().ConfigureAwait(false); } catch { /* Ignore any errors during cleanup */ }
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

        if (wasConnected && this.currentHost is not null)
        {
            this.logger.TcpDisconnected(this.currentHost, this.currentPort);
        }

        GC.SuppressFinalize(this);
    }
}