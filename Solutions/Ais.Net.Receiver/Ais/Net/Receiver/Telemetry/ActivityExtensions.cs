// <copyright file="ActivityExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Extension methods for enriching activities with AIS domain-specific context.
/// </summary>
/// <remarks>
/// All semantic convention constants are defined in <see cref="SemanticConventions"/>.
/// </remarks>
public static class ActivityExtensions
{
    /// <summary>
    /// Sets AIS message context on the activity.
    /// </summary>
    /// <remarks>
    /// These go on as span attributes, including the high-cardinality MMSI. Span attributes are stored
    /// per span record rather than defining a metric time series, so a distinct value costs one
    /// indexed field on one span and nothing ongoing - and identifying a specific vessel is precisely
    /// what trace search is for. Cardinality discipline belongs on metric dimensions
    /// (see <see cref="ApplicationMetrics"/>), not here.
    /// </remarks>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="messageType">The AIS message type.</param>
    /// <param name="mmsi">The Maritime Mobile Service Identity.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetAisMessageContext(this Activity? activity, int messageType, uint mmsi)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        activity.SetTag(SemanticConventions.Ais.MessageType, messageType);
        activity.SetTag(SemanticConventions.Messaging.System, "ais");
        activity.SetTag(SemanticConventions.Ais.Mmsi, mmsi);

        return activity;
    }

    /// <summary>
    /// Sets vessel identity information on the activity as span attributes, so a trace can be found by
    /// vessel name or call sign - the latter often being the only usable identifier when the
    /// transmitted name is blank or garbled.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="vesselName">The vessel name.</param>
    /// <param name="callSign">The vessel call sign.</param>
    /// <param name="shipType">The ship type code.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetVesselIdentity(
        this Activity? activity,
        string? vesselName,
        string? callSign,
        int? shipType)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        if (shipType.HasValue)
        {
            activity.SetTag(SemanticConventions.Ais.ShipType, shipType.Value);
        }

        if (!string.IsNullOrWhiteSpace(vesselName))
        {
            activity.SetTag(SemanticConventions.Ais.VesselName, vesselName);
        }

        if (!string.IsNullOrWhiteSpace(callSign))
        {
            activity.SetTag(SemanticConventions.Ais.CallSign, callSign);
        }

        return activity;
    }

    /// <summary>
    /// Sets vessel position on the activity as span attributes, so traces can be filtered to a
    /// geographic area.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="latitude">The latitude in degrees.</param>
    /// <param name="longitude">The longitude in degrees.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetVesselPosition(this Activity? activity, double? latitude, double? longitude)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        if (latitude.HasValue && longitude.HasValue)
        {
            activity.SetTag(SemanticConventions.Ais.PositionLatitude, latitude.Value);
            activity.SetTag(SemanticConventions.Ais.PositionLongitude, longitude.Value);
        }

        return activity;
    }

    /// <summary>
    /// Sets navigation status on the activity.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="status">The navigation status code.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetNavigationStatus(this Activity? activity, int? status)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        if (status.HasValue)
        {
            activity.SetTag(SemanticConventions.Ais.NavigationStatus, status.Value);
        }

        return activity;
    }

    /// <summary>
    /// Sets station metadata on the activity as span attributes, including the message id that
    /// correlates a span with the source sentence.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="stationId">The station identifier.</param>
    /// <param name="unixTimestamp">The Unix timestamp of the message.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetStationMetadata(this Activity? activity, int stationId, long unixTimestamp)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        activity.SetTag(SemanticConventions.Ais.StationId, stationId);
        activity.SetTag(SemanticConventions.Ais.Timestamp, unixTimestamp);
        activity.SetTag(SemanticConventions.Messaging.MessageId, $"{stationId}-{unixTimestamp}");

        return activity;
    }

    /// <summary>
    /// Records an exception with full context following OpenTelemetry semantic conventions.
    /// </summary>
    /// <param name="activity">The activity to record the exception on.</param>
    /// <param name="exception">The exception to record.</param>
    /// <param name="escaped">Whether the exception escaped the scope of the span.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordExceptionWithStatus(
        this Activity? activity,
        Exception exception,
        bool escaped = true)
    {
        if (activity is null)
        {
            return null;
        }

        // Always set error status (lightweight, important for error reporting)
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // Only record detailed exception event if sampling is requesting full data
        if (!activity.IsAllDataRequested)
        {
            return activity;
        }

        // AddException emits the standard "exception" event with the OpenTelemetry exception.*
        // attributes (type, message, stacktrace); we only add exception.escaped on top.
        activity.AddException(exception, new TagList { { SemanticConventions.Exception.Escaped, escaped } });

        return activity;
    }

    /// <summary>
    /// Records a handled exception (non-escaped) with context.
    /// Does not set error status since the exception was handled.
    /// </summary>
    /// <param name="activity">The activity to record the exception on.</param>
    /// <param name="exception">The exception that was handled.</param>
    /// <param name="handlingStrategy">Description of how the exception was handled.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordHandledException(
        this Activity? activity,
        Exception exception,
        string handlingStrategy)
    {
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        var tags = new ActivityTagsCollection
        {
            { SemanticConventions.Exception.Type, exception.GetType().FullName },
            { SemanticConventions.Exception.Message, exception.Message },
            { SemanticConventions.Exception.Escaped, false },
            { SemanticConventions.Exception.HandlingStrategy, handlingStrategy },
        };

        activity.AddEvent(new ActivityEvent("exception.handled", tags: tags));

        return activity;
    }

    /// <summary>
    /// Records an exception with all inner exceptions following OpenTelemetry semantic conventions.
    /// Recursively records the entire exception chain with depth tracking.
    /// </summary>
    /// <param name="activity">The activity to record the exception on.</param>
    /// <param name="exception">The exception to record.</param>
    /// <param name="escaped">Whether the exception escaped the scope of the span.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordExceptionWithInnerExceptions(
        this Activity? activity,
        Exception exception,
        bool escaped = true)
    {
        if (activity is null)
        {
            return null;
        }

        // Always set error status (lightweight, important for error reporting)
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // Only record detailed exception events if sampling is requesting full data
        if (!activity.IsAllDataRequested)
        {
            return activity;
        }

        // The outermost frame goes through AddException so it produces the standard "exception"
        // event with the canonical exception.* attributes. Inner frames are emitted by hand under a
        // distinct event name, which AddException cannot express.
        activity.AddException(
            exception,
            new TagList
            {
                { SemanticConventions.Exception.Escaped, escaped },
                { SemanticConventions.Exception.Depth, 0 },
            });

        int depth = 1;

        for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
        {
            activity.AddEvent(new ActivityEvent(
                "exception.inner",
                tags: new ActivityTagsCollection
                {
                    { SemanticConventions.Exception.Type, current.GetType().FullName ?? current.GetType().Name },
                    { SemanticConventions.Exception.Message, current.Message },
                    { SemanticConventions.Exception.Stacktrace, current.StackTrace ?? string.Empty },
                    { SemanticConventions.Exception.Escaped, escaped },
                    { SemanticConventions.Exception.Depth, depth },
                }));

            depth++;
        }

        return activity;
    }

    /// <summary>
    /// Sets an error type tag on the activity for categorizing errors.
    /// </summary>
    /// <param name="activity">The activity to tag.</param>
    /// <param name="errorType">The error type category.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? SetErrorType(this Activity? activity, string errorType)
    {
        activity?.SetTag(SemanticConventions.Error.Type, errorType);
        return activity;
    }

    /// <summary>
    /// Records a vessel detection business event.
    /// Used when a new MMSI is first observed in the AIS stream.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="mmsi">The Maritime Mobile Service Identity of the detected vessel.</param>
    /// <param name="vesselName">The vessel name if available.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordVesselDetected(
        this Activity? activity,
        uint mmsi,
        string? vesselName = null)
    {
        if (activity is null)
        {
            return null;
        }

        var tags = new ActivityTagsCollection
        {
            { SemanticConventions.Event.Name, "vessel.detected" },
            { SemanticConventions.Ais.Mmsi, mmsi },
        };

        if (!string.IsNullOrWhiteSpace(vesselName))
        {
            tags.Add(SemanticConventions.Ais.VesselName, vesselName);
        }

        activity.AddEvent(new ActivityEvent("vessel.detected", tags: tags));
        return activity;
    }

    /// <summary>
    /// Records a storage blob rotation business event.
    /// Used when switching to a new storage blob due to time-based rotation.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="oldBlobPath">The path of the previous blob.</param>
    /// <param name="newBlobPath">The path of the new blob.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordBlobRotation(
        this Activity? activity,
        string oldBlobPath,
        string newBlobPath)
    {
        if (activity is null)
        {
            return null;
        }

        activity.AddEvent(new ActivityEvent("storage.blob.rotated",
            tags: new ActivityTagsCollection
            {
                { SemanticConventions.Event.Name, "storage.blob.rotated" },
                { SemanticConventions.Ais.Storage.OldBlobPath, oldBlobPath },
                { SemanticConventions.Ais.Storage.NewBlobPath, newBlobPath },
            }));

        return activity;
    }

    /// <summary>
    /// Records a connection state change business event.
    /// Used to track when the AIS receiver connects or disconnects from the data source.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="connected">Whether the connection is now established.</param>
    /// <param name="reason">The reason for the state change.</param>
    /// <param name="host">The host being connected to.</param>
    /// <param name="port">The port being connected to.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordConnectionStateEvent(
        this Activity? activity,
        bool connected,
        string reason,
        string? host = null,
        int? port = null)
    {
        if (activity is null)
        {
            return null;
        }

        var tags = new ActivityTagsCollection
        {
            { SemanticConventions.Event.Name, "connection.state_changed" },
            { SemanticConventions.Connection.State, connected ? "connected" : "disconnected" },
            { SemanticConventions.Connection.StateChangeReason, reason },
        };

        if (!string.IsNullOrWhiteSpace(host))
        {
            tags.Add(SemanticConventions.Network.ServerAddress, host);
        }

        if (port.HasValue)
        {
            tags.Add(SemanticConventions.Network.ServerPort, port.Value);
        }

        activity.AddEvent(new ActivityEvent("connection.state_changed", tags: tags));
        return activity;
    }

    /// <summary>
    /// Records a batch completion business event.
    /// Used when a batch of messages has been processed or written to storage.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="batchSize">The number of messages in the completed batch.</param>
    /// <param name="durationMs">The time taken to process the batch in milliseconds.</param>
    /// <param name="batchType">The type of batch (e.g., "storage", "processing").</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordBatchCompleted(
        this Activity? activity,
        int batchSize,
        double durationMs,
        string batchType = "processing")
    {
        if (activity is null)
        {
            return null;
        }

        activity.AddEvent(new ActivityEvent("batch.completed",
            tags: new ActivityTagsCollection
            {
                { SemanticConventions.Event.Name, "batch.completed" },
                { SemanticConventions.Batch.Type, batchType },
                { SemanticConventions.Batch.Size, batchSize },
                { SemanticConventions.Batch.DurationMs, durationMs },
            }));

        return activity;
    }

    /// <summary>
    /// Records a vessel track lost business event.
    /// Used when a vessel has not been seen for a configured timeout period.
    /// </summary>
    /// <param name="activity">The activity to enrich.</param>
    /// <param name="mmsi">The Maritime Mobile Service Identity of the lost vessel.</param>
    /// <param name="lastSeenTimestamp">When the vessel was last observed.</param>
    /// <param name="timeoutSeconds">The inactivity timeout that triggered the event.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordVesselTrackLost(
        this Activity? activity,
        uint mmsi,
        DateTimeOffset lastSeenTimestamp,
        double timeoutSeconds)
    {
        if (activity is null)
        {
            return null;
        }

        activity.AddEvent(new ActivityEvent("vessel.track_lost",
            tags: new ActivityTagsCollection
            {
                { SemanticConventions.Event.Name, "vessel.track_lost" },
                { SemanticConventions.Ais.Mmsi, mmsi },
                { "vessel.last_seen", lastSeenTimestamp.ToUnixTimeSeconds() },
                { "vessel.timeout_seconds", timeoutSeconds },
            }));

        return activity;
    }

    // The baggage helpers that used to sit here (SetAisBaggage, SetBatchBaggage,
    // GetStationIdFromBaggage, GetBatchIdFromBaggage) were removed rather than wired up, because this
    // topology gives them nowhere correct to go:
    //
    //  - Baggage flows to child activities and, via the W3C 'baggage' header, out of the process. The
    //    only outbound calls here are to Azure Blob Storage, so the effect would be to attach internal
    //    station and batch identifiers to every storage request - a per-request wire cost and a small
    //    information leak to a third party, for no local benefit.
    //  - The getters could never observe what the setters wrote. Storage writes are deliberately
    //    decoupled from message decoding by the bounded queue in StorageBatchPipeline, so a
    //    StorageWrite span is not a child of any ProcessMessage span and no baggage crosses that
    //    boundary; GetStationIdFromBaggage would have returned null in the one place it was meant for.
    //  - deployment.environment is already a resource attribute (see ServiceDefaults), so carrying it
    //    in baggage would duplicate it on every span.
    //
    // Station id and batch context are on the spans that own them instead (SetStationMetadata and
    // RecordBatchCompleted).
}
