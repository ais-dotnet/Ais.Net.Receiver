// <copyright file="NmeaLineReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.IO.Pipelines;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Splits a byte stream of newline-delimited NMEA sentences into lines. This is the single line
/// framer shared by every transport: bytes pass through untouched (no text decoding), lines are
/// split on <c>\n</c> with one trailing <c>\r</c> trimmed, and line length is capped so a peer that
/// never sends a newline cannot grow the buffer without limit.
/// </summary>
public sealed class NmeaLineReader : IAsyncDisposable
{
    /// <summary>
    /// The default maximum line length, in bytes. A single NMEA line is well under this; the cap
    /// only exists to bound memory if a feed sends a long burst of bytes with no newline.
    /// </summary>
    public const int DefaultMaxLineLength = 8192;

    private readonly PipeReader reader;
    private readonly ILogger logger;
    private readonly int maxLineLength;
    private byte[] lineBuffer = new byte[512];
    private bool disposed;

    // Set when an over-long, unterminated line has been dropped: everything up to and including the
    // next newline is the remainder of that line, so it is discarded too rather than surfacing as a
    // garbage fragment.
    private bool resyncing;

    /// <summary>
    /// Initializes a new instance of the <see cref="NmeaLineReader"/> class over a
    /// <see cref="PipeReader"/>. The line reader completes the pipe reader when disposed.
    /// </summary>
    /// <param name="reader">The pipe reader supplying the bytes.</param>
    /// <param name="logger">An optional logger, used to report discarded over-long lines.</param>
    /// <param name="maxLineLength">The maximum line length, in bytes; longer lines are discarded.</param>
    public NmeaLineReader(PipeReader reader, ILogger? logger = null, int maxLineLength = DefaultMaxLineLength)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineLength);

        this.reader = reader;
        this.logger = logger ?? NullLogger.Instance;
        this.maxLineLength = maxLineLength;
    }

    /// <summary>
    /// Creates a line reader over a <see cref="Stream"/>. The stream is left open when the line
    /// reader is disposed: the caller owns it.
    /// </summary>
    /// <param name="stream">The stream supplying the bytes.</param>
    /// <param name="logger">An optional logger, used to report discarded over-long lines.</param>
    /// <param name="maxLineLength">The maximum line length, in bytes; longer lines are discarded.</param>
    /// <returns>The line reader.</returns>
    public static NmeaLineReader Create(Stream stream, ILogger? logger = null, int maxLineLength = DefaultMaxLineLength)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // leaveOpen: the caller owns the stream and disposes it after disposing this reader; letting
        // the PipeReader also own it would double-dispose.
        return new NmeaLineReader(
            PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 4096, minimumReadSize: 512, leaveOpen: true)),
            logger,
            maxLineLength);
    }

    /// <summary>
    /// Reads the next line, returning <see langword="null"/> at end of stream.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The line without its terminator, or <see langword="null"/> at end of stream. An empty line
    /// between consecutive newlines is returned as empty memory. A final unterminated line is
    /// returned if it is non-empty and within the length cap.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A newline-terminated line longer than the cap is discarded (and logged) and reading
    /// continues. If more than the cap is buffered with no newline, that data is discarded (and
    /// logged) along with the rest of the line up to its newline, and framing resumes after it.
    /// </para>
    /// <para>
    /// The returned memory is a slice of a reusable, growable buffer owned by this reader. It is
    /// only valid until the next call to <see cref="ReadLineAsync"/> or
    /// <see cref="DisposeAsync"/>; callers that need the data beyond that must copy it.
    /// </para>
    /// </remarks>
    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        while (true)
        {
            ReadResult result = await this.reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (position != null)
            {
                ReadOnlySequence<byte> lineSlice = buffer.Slice(0, position.Value);
                SequencePosition afterNewline = buffer.GetPosition(1, position.Value);

                if (this.resyncing)
                {
                    // The tail of an over-long line already dropped (and logged) below: discard it
                    // too, and resume normal framing after this newline.
                    this.resyncing = false;
                    this.reader.AdvanceTo(afterNewline);
                    continue;
                }

                // A newline-terminated line that is still absurdly long is malformed; drop it rather
                // than surface (and buffer) garbage.
                if (lineSlice.Length > this.maxLineLength)
                {
                    this.logger.NmeaLineDiscarded(ClampLength(lineSlice.Length), this.maxLineLength);
                    this.reader.AdvanceTo(afterNewline);
                    continue;
                }

                // Found a line. Copy it into a reusable buffer (grown as needed) rather than
                // allocating a fresh array per line. The returned memory is only valid until the
                // next ReadLineAsync/DisposeAsync call.
                ReadOnlyMemory<byte> line = this.CopyLine(lineSlice);
                this.reader.AdvanceTo(afterNewline);
                return line;
            }

            if (result.IsCompleted)
            {
                // The stream closed with no further newline. Emit any final, unterminated line
                // (StreamReader.ReadLineAsync did) before signalling end of stream, unless it is over
                // the length cap (or the tail of a line already dropped), in which case drop it.
                if (buffer.Length == 0 || buffer.Length > this.maxLineLength || this.resyncing)
                {
                    if (buffer.Length > 0)
                    {
                        this.reader.AdvanceTo(buffer.End);
                    }

                    this.resyncing = false;
                    return null;
                }

                ReadOnlyMemory<byte> line = this.CopyLine(buffer);
                this.reader.AdvanceTo(buffer.End);
                return line;
            }

            // No newline yet. Bound the buffered length so a feed that never sends '\n' cannot grow
            // the pipe without limit; drop the over-long partial line and resync on the next newline.
            if (this.resyncing)
            {
                // Still inside a line already dropped: keep discarding until its newline arrives.
                this.reader.AdvanceTo(buffer.End);
                continue;
            }

            if (buffer.Length > this.maxLineLength)
            {
                this.logger.NmeaLineDiscarded(ClampLength(buffer.Length), this.maxLineLength);
                this.reader.AdvanceTo(buffer.End);
                this.resyncing = true;
                continue;
            }

            this.reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// Completes the underlying <see cref="PipeReader"/>. Safe to call more than once.
    /// </summary>
    /// <returns>A task that completes when the reader has been completed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        try
        {
            await this.reader.CompleteAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore any errors during cleanup.
        }
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
}
