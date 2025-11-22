// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Receiver;

public class FileStreamNmeaReceiver : INmeaReceiver
{
    private readonly string path;
    private readonly TimeSpan delay = TimeSpan.Zero;

    public FileStreamNmeaReceiver(string path)
    {
        this.path = path;
    }
        
    public FileStreamNmeaReceiver(string path, TimeSpan delay)
    {
        this.path = path;
        this.delay = delay;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream fs = new(this.path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
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