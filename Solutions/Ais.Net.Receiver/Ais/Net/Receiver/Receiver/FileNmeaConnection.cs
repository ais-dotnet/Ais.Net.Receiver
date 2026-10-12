// <copyright file="FileNmeaConnection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

using Spectre.IO;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// A <see cref="NmeaSourceKind.Finite"/> connection that reads NMEA sentences from a file, via
/// Spectre.IO's file system abstraction so tests can substitute an in-memory one.
/// </summary>
public sealed class FileNmeaConnection : StreamNmeaConnection
{
    private readonly IFileSystem fileSystem;
    private readonly FilePath path;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileNmeaConnection"/> class.
    /// </summary>
    /// <param name="fileSystem">The file system to read from.</param>
    /// <param name="path">The path of the file to read.</param>
    /// <param name="lineDelay">An optional delay before each line, e.g. to simulate a live feed from a recording.</param>
    /// <param name="logger">An optional logger.</param>
    public FileNmeaConnection(IFileSystem fileSystem, FilePath path, TimeSpan? lineDelay = null, ILogger? logger = null)
        : base((path ?? throw new ArgumentNullException(nameof(path))).FullPath, lineDelay, logger)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.path = path;
    }

    /// <inheritdoc/>
    protected override ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(this.fileSystem.File.Retrieve(this.path).OpenRead());
}
