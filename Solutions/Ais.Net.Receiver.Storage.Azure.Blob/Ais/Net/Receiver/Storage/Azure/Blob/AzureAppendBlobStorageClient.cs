// <copyright file="AzureAppendBlobStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Telemetry;

using global::Azure.Storage.Blobs;
using global::Azure.Storage.Blobs.Specialized;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Storage.Azure.Blob;

public class AzureAppendBlobStorageClient : IStorageClient
{
    private readonly StorageConfig configuration;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics? metrics;
    private readonly ApplicationInstrumentation? instrumentation;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private BlobContainerClient? blobContainerClient;
    private BlobTarget? currentTarget;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAppendBlobStorageClient"/> class.
    /// </summary>
    /// <param name="configuration">The storage configuration.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AzureAppendBlobStorageClient(StorageConfig configuration, TimeProvider timeProvider)
        : this(configuration, timeProvider, null, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAppendBlobStorageClient"/> class with full instrumentation.
    /// </summary>
    /// <param name="configuration">The storage configuration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="metrics">The application metrics.</param>
    /// <param name="instrumentation">The application instrumentation.</param>
    /// <param name="logger">The logger.</param>
    public AzureAppendBlobStorageClient(
        StorageConfig configuration,
        TimeProvider timeProvider,
        ApplicationMetrics? metrics,
        ApplicationInstrumentation? instrumentation,
        ILogger? logger)
    {
        this.configuration = configuration;
        this.timeProvider = timeProvider;
        this.metrics = metrics;
        this.instrumentation = instrumentation;
        this.logger = logger;
    }

    public async Task PersistAsync(IEnumerable<ReadOnlyMemory<byte>> messages)
    {
        using Activity? activity = this.instrumentation?.ActivitySource.StartActivity("StorageWrite");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            // Snapshot the current blob target once; using this local rather than the field means a
            // concurrent hour-boundary swap can never redirect this batch to the wrong blob.
            BlobTarget target = await this.EnsureCurrentHourBlobInitializedAsync().ConfigureAwait(false);

            // Write the raw sentence bytes straight into the block, newline-separated. This avoids
            // the bytes -> string -> UTF-8 round trip the previous StreamWriter path incurred (and,
            // as a bonus, the BOM it used to emit).
            using MemoryStream stream = new();
            int messageCount = 0;
            foreach (ReadOnlyMemory<byte> message in messages)
            {
                stream.Write(message.Span);
                stream.WriteByte((byte)'\n');
                messageCount++;
            }

            long byteCount = stream.Length;
            activity?.SetTag("ais.storage.message_count", messageCount);
            activity?.SetTag("ais.storage.bytes", byteCount);
            activity?.SetTag("ais.storage.blob_path", target.Path);

            this.logger?.WritingBatch(messageCount, byteCount, target.Path);

            stream.Position = 0;
            await target.Client.AppendBlockAsync(stream).ConfigureAwait(false);

            // Record metrics
            this.metrics?.StorageWriteOperations.Add(1);
            this.metrics?.StorageBytesWritten.Add(byteCount);
            this.metrics?.BatchSize.Record(messageCount);

            stopwatch.Stop();
            this.metrics?.StorageWriteDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            this.logger?.StorageWriteCompleted(stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.RecordExceptionWithStatus(ex, escaped: true);
            this.logger?.BlobWriteFailed(ex);
            throw;
        }
    }

    public void Dispose()
    {
        this.initializationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Computes the hourly, date-partitioned blob path for a timestamp
    /// (<c>raw/yyyy/MM/dd/yyyyMMddTHH.nm4</c>).
    /// </summary>
    internal static string GetHourlyBlobPath(DateTimeOffset timestamp) =>
        $"raw/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddTHH}.nm4";

    /// <summary>
    /// Creates the container client. Overridable so tests can substitute a fake without a live account.
    /// </summary>
    protected virtual BlobContainerClient CreateBlobContainerClient() =>
        new(this.configuration.ConnectionString, this.configuration.ContainerName);

    private async Task<BlobTarget> EnsureCurrentHourBlobInitializedAsync()
    {
        string newBlobPath = GetHourlyBlobPath(this.timeProvider.GetUtcNow());

        // Fast path: a single atomic read of the current target (reference reads are atomic).
        BlobTarget? target = this.currentTarget;
        if (target is not null && target.Path == newBlobPath)
        {
            return target;
        }

        using Activity? activity = this.instrumentation?.ActivitySource.StartActivity("BlobInitialization");
        Stopwatch stopwatch = Stopwatch.StartNew();

        await this.initializationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            target = this.currentTarget;
            if (target is not null && target.Path == newBlobPath)
            {
                return target;
            }

            activity?.SetTag("ais.storage.blob_path", newBlobPath);
            this.logger?.InitializingBlob(newBlobPath);

            this.blobContainerClient ??= this.CreateBlobContainerClient();
            AppendBlobClient blobClient = this.blobContainerClient.GetAppendBlobClient(newBlobPath);

            await this.blobContainerClient.CreateIfNotExistsAsync().ConfigureAwait(false);
            await blobClient.CreateIfNotExistsAsync().ConfigureAwait(false);

            target = new BlobTarget(blobClient, newBlobPath);
            this.currentTarget = target; // atomic publish of the new (client, path) pair

            this.logger?.BlobCreated(newBlobPath);

            stopwatch.Stop();
            this.logger?.BlobInitializationCompleted(stopwatch.Elapsed.TotalMilliseconds);
            return target;
        }
        catch (Exception ex)
        {
            activity?.RecordExceptionWithStatus(ex, escaped: true);
            throw;
        }
        finally
        {
            this.initializationLock.Release();
        }
    }

    private sealed record BlobTarget(AppendBlobClient Client, string Path);
}
