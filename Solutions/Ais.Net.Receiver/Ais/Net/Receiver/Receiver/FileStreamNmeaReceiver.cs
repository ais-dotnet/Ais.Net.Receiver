// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Spectre.IO;

namespace Ais.Net.Receiver.Receiver;

public class FileStreamNmeaReceiver : INmeaReceiver, IAsyncDisposable
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
        this.streamReader = new StreamReader(this.fileStream);

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

                yield return System.Text.Encoding.ASCII.GetBytes(line);
            }
        }
        finally
        {
            // Cleanup when enumeration completes normally or is cancelled
            await this.CleanupAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.CleanupAsync();
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
