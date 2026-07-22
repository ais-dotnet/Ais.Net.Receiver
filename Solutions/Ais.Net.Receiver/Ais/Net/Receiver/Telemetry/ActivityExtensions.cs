// <copyright file="ActivityExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Extension methods for enriching activities with AIS domain-specific context.
/// </summary>
public static class ActivityExtensions
{
    // AIS domain semantic conventions (custom namespace)
    private const string AisMessageType = "ais.message.type";
    private const string AisMmsi = "ais.mmsi";
    private const string AisVesselName = "ais.vessel.name";
    private const string AisCallSign = "ais.call_sign";
    private const string AisShipType = "ais.ship_type";
    private const string AisPositionLatitude = "ais.position.latitude";
    private const string AisPositionLongitude = "ais.position.longitude";
    private const string AisNavigationStatus = "ais.navigation_status";
    private const string AisStationId = "ais.station_id";
    private const string AisTimestamp = "ais.timestamp";

    // Standard semantic conventions
    private const string MessagingMessageId = "messaging.message.id";
    private const string MessagingSystem = "messaging.system";

    // Error semantic conventions
    private const string ExceptionType = "exception.type";
    private const string ExceptionMessage = "exception.message";
    private const string ExceptionEscaped = "exception.escaped";
    private const string ErrorType = "error.type";

    /// <summary>
    /// Sets AIS message context on the activity.
    /// </summary>
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

        activity.SetTag(AisMessageType, messageType);
        activity.SetTag(AisMmsi, mmsi);
        activity.SetTag(MessagingSystem, "ais");
        return activity;
    }

    /// <summary>
    /// Sets vessel identity information on the activity.
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

        if (!string.IsNullOrWhiteSpace(vesselName))
        {
            activity.SetTag(AisVesselName, vesselName);
        }

        if (!string.IsNullOrWhiteSpace(callSign))
        {
            activity.SetTag(AisCallSign, callSign);
        }

        if (shipType.HasValue)
        {
            activity.SetTag(AisShipType, shipType.Value);
        }

        return activity;
    }

    /// <summary>
    /// Sets vessel position on the activity.
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
            activity.SetTag(AisPositionLatitude, latitude.Value);
            activity.SetTag(AisPositionLongitude, longitude.Value);
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
            activity.SetTag(AisNavigationStatus, status.Value);
        }

        return activity;
    }

    /// <summary>
    /// Sets station metadata on the activity.
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

        activity.SetTag(AisStationId, stationId);
        activity.SetTag(AisTimestamp, unixTimestamp);
        activity.SetTag(MessagingMessageId, $"{stationId}-{unixTimestamp}");
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

        // Set error status
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        // Record the exception using the built-in helper, which emits the standard
        // "exception" event with the OpenTelemetry exception.* attributes (type, message,
        // stacktrace). We add exception.escaped on top of those.
        activity.AddException(exception, new TagList { { ExceptionEscaped, escaped } });

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
        if (activity is null)
        {
            return null;
        }

        var tags = new ActivityTagsCollection
        {
            { ExceptionType, exception.GetType().FullName },
            { ExceptionMessage, exception.Message },
            { ExceptionEscaped, false },
            { "exception.handling_strategy", handlingStrategy },
        };

        activity.AddEvent(new ActivityEvent("exception.handled", tags: tags));

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
        activity?.SetTag(ErrorType, errorType);
        return activity;
    }
}
