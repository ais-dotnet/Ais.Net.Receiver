// <copyright file="FileStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Spectre.IO;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Reads NMEA sentences from a file, via Spectre.IO's file system abstraction so tests can
/// substitute an in-memory one.
/// </summary>
public class FileStreamNmeaReceiver : StreamNmeaReceiver
{
    private readonly IFileSystem fileSystem;
    private readonly FilePath path;

    public FileStreamNmeaReceiver(IFileSystem fileSystem, FilePath path)
    {
        this.fileSystem = fileSystem;
        this.path = path;
    }

    public FileStreamNmeaReceiver(IFileSystem fileSystem, FilePath path, TimeSpan delay)
        : base(delay)
    {
        this.fileSystem = fileSystem;
        this.path = path;
    }

    /// <inheritdoc/>
    protected override ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(this.fileSystem.File.Retrieve(this.path).OpenRead());
}
