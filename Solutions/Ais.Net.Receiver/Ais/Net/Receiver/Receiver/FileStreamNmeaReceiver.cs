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

public class FileStreamNmeaReceiver : INmeaReceiver
{
    private readonly IFileSystem fileSystem;
    private readonly FilePath path;
    private readonly TimeSpan delay = TimeSpan.Zero;

    public FileStreamNmeaReceiver(IFileSystem fileSystem, string path)
    {
        this.fileSystem = fileSystem;
        this.path = new FilePath(path);
    }
        
    public FileStreamNmeaReceiver(IFileSystem fileSystem, string path, TimeSpan delay)
    {
        this.fileSystem = fileSystem;
        this.path = new FilePath(path);
        this.delay = delay;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var file = this.fileSystem.File.Retrieve(this.path);
        await using Stream fs = file.OpenRead();
        using StreamReader sr = new(fs);

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

            string? line = await sr.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            yield return System.Text.Encoding.ASCII.GetBytes(line);
        }
    }
}