// <copyright file="BlobNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Receiver;

using Azure.Storage.Blobs;

namespace Ais.Net.Receiver.Demo.Tracks.Sources;

/// <summary>
/// Reads NMEA sentences from a blob, so a replay can be driven straight from what the receiver
/// captured rather than from a file someone downloaded first. Blob paths are the ones
/// <c>AzureAppendBlobStorageClient</c> writes: <c>raw/yyyy/MM/dd/yyyyMMddTHH.nm4</c>.
/// </summary>
/// <remarks>
/// The line handling - including the Latin1 round-tripping that keeps tag-block bytes intact - lives
/// in <see cref="StreamNmeaReceiver"/>; this class only knows how to open the blob.
/// </remarks>
public sealed class BlobNmeaReceiver : StreamNmeaReceiver
{
    private readonly BlobClient blobClient;

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
    protected override async ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken) =>
        await this.blobClient.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
}
