// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using Spectre.IO;

namespace Ais.Net.Receiver.Receiver;

public class FileStreamNmeaReceiver : INmeaReceiver
{
    private readonly IFileSystem fileSystem;
    private readonly FilePath path;
    private readonly TimeSpan delay = TimeSpan.Zero;

    private Stream? fileStream;
    private StreamReader? streamReader;

    public FileStreamNmeaReceiver(IFileSystem fileSystem, FilePath path)
    {
        this.fileSystem = fileSystem;
        this.path = path;
    }

    public FileStreamNmeaReceiver(IFileSystem fileSystem, FilePath path, TimeSpan delay)
    {
        this.fileSystem = fileSystem;
        this.path = path;
        this.delay = delay;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var file = this.fileSystem.File.Retrieve(this.path);
        this.fileStream = file.OpenRead();

        // Latin1 maps each byte 0-255 to the same-valued char and back, so reading here and
        // re-encoding below round-trips the original bytes losslessly (ASCII would corrupt any
        // byte > 0x7F - e.g. in a tag block - to '?').
        this.streamReader = new StreamReader(this.fileStream, System.Text.Encoding.Latin1);

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

                yield return System.Text.Encoding.Latin1.GetBytes(line);
            }
        }
        finally
        {
            // Cleanup when enumeration completes normally or is cancelled
            await this.CleanupAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.CleanupAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask CleanupAsync()
    {
        if (this.streamReader is not null)
        {
            this.streamReader.Dispose();
            this.streamReader = null;
        }

        if (this.fileStream is not null)
        {
            await this.fileStream.DisposeAsync();
            this.fileStream = null;
        }
    }
}