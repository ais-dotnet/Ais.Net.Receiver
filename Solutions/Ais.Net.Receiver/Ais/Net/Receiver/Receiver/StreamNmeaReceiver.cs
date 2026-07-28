// <copyright file="StreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using System.Text;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Base class for receivers that read NMEA sentences line by line from a <see cref="Stream"/> - a
/// local file, a blob, anything a derived class can open. Owns the line loop, the optional
/// inter-line delay, and disposal; a derived class supplies only the stream.
/// </summary>
/// <remarks>
/// Latin1 maps each byte 0-255 to the same-valued char and back, so reading lines as text here and
/// re-encoding them on the way out round-trips the original bytes losslessly (ASCII would corrupt
/// any byte > 0x7F - e.g. in a tag block - to '?').
/// </remarks>
public abstract class StreamNmeaReceiver : INmeaReceiver
{
    private readonly TimeSpan delay = TimeSpan.Zero;

    private Stream? stream;
    private StreamReader? streamReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNmeaReceiver"/> class that reads as fast
    /// as the stream allows.
    /// </summary>
    protected StreamNmeaReceiver()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNmeaReceiver"/> class that pauses between
    /// lines, e.g. to simulate a live feed from a recording.
    /// </summary>
    /// <param name="delay">The delay before each line.</param>
    protected StreamNmeaReceiver(TimeSpan delay)
    {
        this.delay = delay;
    }

    /// <summary>
    /// Reads the stream's NMEA sentences, one line at a time.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The sentences, as the original bytes.</returns>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.stream = await this.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        this.streamReader = new StreamReader(this.stream, Encoding.Latin1);

        try
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }

                if (this.delay > TimeSpan.Zero)
                {
                    await Task.Delay(this.delay, cancellationToken).ConfigureAwait(false);
                }

                string? line = await this.streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                yield return Encoding.Latin1.GetBytes(line);
            }
        }
        finally
        {
            // Cleanup when enumeration completes normally or is cancelled
            await this.CleanupAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.CleanupAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Opens the stream of NMEA sentences to read. Called once, at the start of enumeration; the
    /// base class owns the returned stream and disposes it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stream.</returns>
    protected abstract ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken);

    private async ValueTask CleanupAsync()
    {
        if (this.streamReader is not null)
        {
            this.streamReader.Dispose();
            this.streamReader = null;
        }

        if (this.stream is not null)
        {
            await this.stream.DisposeAsync().ConfigureAwait(false);
            this.stream = null;
        }
    }
}
