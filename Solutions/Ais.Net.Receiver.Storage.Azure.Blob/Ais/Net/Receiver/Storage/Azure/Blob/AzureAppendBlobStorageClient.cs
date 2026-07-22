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
    private AppendBlobClient? appendBlobClient;
    private BlobContainerClient? blobContainerClient;
    private string? currentBlobPath;

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
            await this.EnsureCurrentHourBlobInitializedAsync().ConfigureAwait(false);

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
            activity?.SetTag("ais.storage.blob_path", this.currentBlobPath);

            this.logger?.WritingBatch(messageCount, byteCount, this.currentBlobPath ?? "unknown");

            stream.Position = 0;
            await this.appendBlobClient!.AppendBlockAsync(stream).ConfigureAwait(false);

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

    private async Task EnsureCurrentHourBlobInitializedAsync()
    {
        DateTimeOffset timestamp = this.timeProvider.GetUtcNow();
        string newBlobPath = $"raw/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddTHH}.nm4";

        if (this.appendBlobClient is not null && this.currentBlobPath == newBlobPath)
        {
            return;
        }

        using Activity? activity = this.instrumentation?.ActivitySource.StartActivity("BlobInitialization");
        Stopwatch stopwatch = Stopwatch.StartNew();

        await this.initializationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (this.appendBlobClient is not null && this.currentBlobPath == newBlobPath)
            {
                return;
            }

            this.currentBlobPath = newBlobPath;
            activity?.SetTag("ais.storage.blob_path", newBlobPath);

            this.logger?.InitializingBlob(newBlobPath);

            this.blobContainerClient ??= new BlobContainerClient(
                this.configuration.ConnectionString,
                this.configuration.ContainerName);

            this.appendBlobClient = this.blobContainerClient.GetAppendBlobClient(newBlobPath);

            await this.blobContainerClient.CreateIfNotExistsAsync().ConfigureAwait(false);
            await this.appendBlobClient.CreateIfNotExistsAsync().ConfigureAwait(false);

            this.logger?.BlobCreated(newBlobPath);

            stopwatch.Stop();
            this.logger?.BlobInitializationCompleted(stopwatch.Elapsed.TotalMilliseconds);
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
}
