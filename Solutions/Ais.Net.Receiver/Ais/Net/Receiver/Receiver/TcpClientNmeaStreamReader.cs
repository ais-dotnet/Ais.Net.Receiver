// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Receiver;

public class TcpClientNmeaStreamReader : INmeaStreamReader
{
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private PipeReader? reader;

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
            this.reader = PipeReader.Create(this.stream);
        }
        catch (Exception)
        {
            // If connection fails, clean up resources
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
                // Found a line
                ReadOnlySequence<byte> line = buffer.Slice(0, position.Value);
                
                // Copy to array to return (simplest for now to avoid lifetime issues)
                byte[] lineBytes = line.ToArray();

                // Trim \r if present
                int length = lineBytes.Length;
                if (length > 0 && lineBytes[length - 1] == '\r')
                {
                    // Advance reader past the newline
                    this.reader.AdvanceTo(buffer.GetPosition(1, position.Value));
                    
                    return new ReadOnlyMemory<byte>(lineBytes, 0, length - 1);
                }

                // Advance reader past the newline
                this.reader.AdvanceTo(buffer.GetPosition(1, position.Value));

                return lineBytes;
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

        GC.SuppressFinalize(this);
    }
}
