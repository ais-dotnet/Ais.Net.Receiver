// <copyright file="BlobNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Azure.Storage.Blobs;

namespace Ais.Net.Receiver.Demo.Tracks.Sources;

/// <summary>
/// Reads NMEA sentences from a blob, so a replay can be driven straight from what the receiver
/// captured rather than from a file someone downloaded first. Blob paths are the ones
/// <c>AzureAppendBlobStorageClient</c> writes: <c>raw/yyyy/MM/dd/yyyyMMddTHH.nm4</c>.
/// </summary>
/// <remarks>
/// This mirrors <see cref="FileStreamNmeaReceiver"/> deliberately, including its Latin1 handling: that
/// encoding maps each byte 0-255 to the same-valued char and back, so reading lines as text and
/// re-encoding them round-trips the original bytes. ASCII would corrupt any byte above 0x7F - which
/// includes tag-block content the pipeline parses timestamps out of - into '?'.
/// </remarks>
public sealed class BlobNmeaReceiver : INmeaReceiver
{
    private readonly BlobClient blobClient;

    private Stream? blobStream;
    private StreamReader? streamReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobNmeaReceiver"/> class.
    /// </summary>
    /// <param name="connectionString">The storage connection string. The demo reuses the receiver's own <c>Storage:ConnectionString</c>, so this works against Azurite unchanged.</param>
    /// <param name="containerName">The blob container.</param>
    /// <param name="blobPath">The path of the blob to replay.</param>
    public BlobNmeaReceiver(string connectionString, string containerName, string blobPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        this.blobClient = new BlobContainerClient(connectionString, containerName).GetBlobClient(blobPath);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.blobStream = await this.blobClient.OpenReadAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        this.streamReader = new StreamReader(this.blobStream, Encoding.Latin1);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
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
            await this.CleanupAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
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

        if (this.blobStream is not null)
        {
            await this.blobStream.DisposeAsync().ConfigureAwait(false);
            this.blobStream = null;
        }
    }
}
