// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Storage.Azure.Blob;

/// <summary>
/// Source-generated logging methods for the Azure Blob storage component.
/// </summary>
internal static partial class Log
{
    // Storage operations (2000-2009)
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Initializing blob: {BlobPath}")]
    public static partial void InitializingBlob(this ILogger logger, string blobPath);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Blob created: {BlobPath}")]
    public static partial void BlobCreated(this ILogger logger, string blobPath);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Writing batch of {MessageCount} messages ({ByteCount} bytes) to {BlobPath}")]
    public static partial void WritingBatch(this ILogger logger, int messageCount, long byteCount, string blobPath);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Failed to write to blob storage")]
    public static partial void BlobWriteFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Blob container creation failed, retrying")]
    public static partial void ContainerCreationRetry(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Blob container created: {ContainerName}")]
    public static partial void ContainerCreated(this ILogger logger, string containerName);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Debug,
        Message = "Storage write completed in {DurationMs}ms")]
    public static partial void StorageWriteCompleted(this ILogger logger, double durationMs);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Debug,
        Message = "Blob initialization completed in {DurationMs}ms")]
    public static partial void BlobInitializationCompleted(this ILogger logger, double durationMs);

    // Storage health check events (2010-2019)
    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Debug,
        Message = "Storage health check completed: {Status}")]
    public static partial void StorageHealthCheckCompleted(this ILogger logger, string status);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Warning,
        Message = "Storage health check degraded: {Reason}")]
    public static partial void StorageHealthCheckDegraded(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 2012,
        Level = LogLevel.Error,
        Message = "Storage health check failed")]
    public static partial void StorageHealthCheckFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2013,
        Level = LogLevel.Debug,
        Message = "Storage capture is disabled, skipping health check")]
    public static partial void StorageCaptureDisabled(this ILogger logger);
}
