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
    // A single NMEA line is well under this; the cap only exists to bound memory if a feed sends a
    // long burst of bytes with no newline (a malformed or hostile peer) so the pipe cannot grow
    // without limit.
    private const int MaxLineLength = 8192;

    // Bounds how long a connect attempt may hang before we give up and let the caller's retry
    // loop schedule another. A blocked SYN can otherwise stall well past any useful timeout.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger logger;
    private byte[] lineBuffer = new byte[512];
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private PipeReader? reader;
    private string? currentHost;
    private int currentPort;

    // Diagnostic: logs the shape of the first read after each connect, so a feed that accepts the
    // connection but never sends is distinguishable from one that was never reachable. Reset on
    // connect rather than per ReadLineAsync call, which would log on every line.
    private bool firstReadPending;

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
            ReceiveBufferSize = 65_536,             // 64KB buffer for bursty traffic
            SendBufferSize = 8_192,                 // We send nothing of consequence; keep it small
            ReceiveTimeout = 120_000,               // Socket-level safety net beneath the idle timeout
            SendTimeout = 30_000,
            NoDelay = true,                         // Disable Nagle's algorithm for lower latency
            LingerState = new LingerOption(true, 5) // Allow up to 5s to flush on close
        };

        // Keepalive is what actually detects a half-open connection: an AIS feed that silently
        // disappears leaves the socket readable-but-idle, and without probes we would wait for the
        // idle timeout on every drop.
        Socket socket = this.tcpClient.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);

        // Retry count is settable on Linux and macOS only; Windows fixes it at 10.
        if (!OperatingSystem.IsWindows())
        {
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }

        this.logger.TcpSocketConfigured(host, port);

        try
        {
            this.logger.TcpConnecting(host, port);

            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);

            await this.tcpClient.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            this.stream = this.tcpClient.GetStream();

            // leaveOpen: this type disposes the stream itself in DisposeAsync, after completing the
            // reader; letting the PipeReader also own it would double-dispose.
            this.reader = PipeReader.Create(
                this.stream,
                new StreamPipeReaderOptions(bufferSize: 4096, minimumReadSize: 512, leaveOpen: true));

            this.firstReadPending = true;
            this.logger.TcpConnected(host, port);
        }
        catch (SocketException ex)
        {
            // Classify the common failures so operators can tell "feed is down" from "DNS is wrong"
            // from "we cannot route there" without reading stack traces.
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    this.logger.TcpConnectionRefused(host, port);
                    break;
                case SocketError.HostNotFound:
                    this.logger.TcpHostNotFound(host, port);
                    break;
                case SocketError.TimedOut:
                    this.logger.TcpConnectionTimedOut(host, port);
                    break;
                case SocketError.NetworkUnreachable:
                    this.logger.TcpNetworkUnreachable(host, port);
                    break;
                case SocketError.ConnectionReset:
                    this.logger.TcpConnectionReset(host, port);
                    break;
                case SocketError.ConnectionAborted:
                    this.logger.TcpConnectionAborted(host, port);
                    break;
                default:
                    this.logger.TcpConnectionFailed(ex, host, port);
                    break;
            }

            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The connect timeout elapsed rather than the caller cancelling. Surface it as a
            // timeout so the retry loop treats it like any other connection failure.
            this.logger.TcpConnectionTimedOut(host, port);
            await this.DisposeAsync().ConfigureAwait(false);
            throw new SocketException((int)SocketError.TimedOut);
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

            if (this.firstReadPending)
            {
                this.firstReadPending = false;
                this.logger.TcpFirstRead(this.currentHost ?? "unknown", this.currentPort, buffer.Length, result.IsCompleted);
            }

            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (position != null)
            {
                ReadOnlySequence<byte> lineSlice = buffer.Slice(0, position.Value);
                SequencePosition afterNewline = buffer.GetPosition(1, position.Value);

                // A newline-terminated line that is still absurdly long is malformed; drop it rather
                // than surface (and buffer) garbage.
                if (lineSlice.Length > MaxLineLength)
                {
                    this.logger.NmeaLineDiscarded(ClampLength(lineSlice.Length), MaxLineLength);
                    this.reader.AdvanceTo(afterNewline);
                    continue;
                }

                // Found a line. Copy it into a reusable buffer (grown as needed) rather than
                // allocating a fresh array per line. The returned memory is only valid until the
                // next ReadLineAsync/DisposeAsync call, as documented on INmeaStreamReader.
                ReadOnlyMemory<byte> line = this.CopyLine(lineSlice);
                this.reader.AdvanceTo(afterNewline);
                return line;
            }

            if (result.IsCompleted)
            {
                // The stream closed with no further newline. Emit any final, unterminated line
                // (StreamReader.ReadLineAsync did) before signalling end of stream, unless it is over
                // the length cap, in which case drop it.
                if (buffer.Length == 0 || buffer.Length > MaxLineLength)
                {
                    if (buffer.Length > 0)
                    {
                        this.reader.AdvanceTo(buffer.End);
                    }

                    break;
                }

                ReadOnlyMemory<byte> line = this.CopyLine(buffer);
                this.reader.AdvanceTo(buffer.End);
                return line;
            }

            // No newline yet. Bound the buffered length so a feed that never sends '\n' cannot grow
            // the pipe without limit; drop the over-long partial line and resync on the next newline.
            if (buffer.Length > MaxLineLength)
            {
                this.logger.NmeaLineDiscarded(ClampLength(buffer.Length), MaxLineLength);
                this.reader.AdvanceTo(buffer.End);
                continue;
            }

            this.reader.AdvanceTo(buffer.Start, buffer.End);
        }

        this.logger.TcpStreamEof(this.currentHost ?? "unknown", this.currentPort);
        return null;
    }

    private static int ClampLength(long length) => (int)Math.Min(length, int.MaxValue);

    private ReadOnlyMemory<byte> CopyLine(ReadOnlySequence<byte> line)
    {
        int length = (int)line.Length;

        if (length > this.lineBuffer.Length)
        {
            this.lineBuffer = new byte[Math.Max(length, this.lineBuffer.Length * 2)];
        }

        line.CopyTo(this.lineBuffer);

        // Trim a trailing '\r' (CRLF line endings) if present.
        if (length > 0 && this.lineBuffer[length - 1] == (byte)'\r')
        {
            length--;
        }

        return this.lineBuffer.AsMemory(0, length);
    }

    public async ValueTask DisposeAsync()
    {
        bool wasConnected = this.tcpClient?.Connected == true;

        this.firstReadPending = false;

        // Send FIN before tearing the stream down, so the peer sees an orderly close rather than a
        // reset. Must happen while the socket is still open, hence before the stream is disposed.
        if (wasConnected && this.tcpClient?.Client is { } socket)
        {
            try
            {
                this.logger.TcpGracefulShutdown(this.currentHost ?? "unknown", this.currentPort);
                socket.Shutdown(SocketShutdown.Both);
            }
            catch { /* Ignore shutdown errors - the peer may already be gone */ }
        }

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