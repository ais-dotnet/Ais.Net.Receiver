// <copyright file="SemanticConventions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Centralized semantic convention constants for OpenTelemetry instrumentation.
/// </summary>
/// <remarks>
/// This class defines constants for attribute names used in traces, metrics, and logs
/// following OpenTelemetry semantic conventions (stable v1.25+) and custom AIS domain conventions.
/// </remarks>
public static class SemanticConventions
{
    /// <summary>
    /// Network semantic conventions (stable).
    /// </summary>
    public static class Network
    {
        /// <summary>Server address - domain name or IP.</summary>
        public const string ServerAddress = "server.address";

        /// <summary>Server port number.</summary>
        public const string ServerPort = "server.port";

        /// <summary>Transport protocol (e.g., "tcp", "udp").</summary>
        public const string Transport = "network.transport";
    }

    /// <summary>
    /// Messaging semantic conventions.
    /// </summary>
    public static class Messaging
    {
        /// <summary>Unique message identifier.</summary>
        public const string MessageId = "messaging.message.id";

        /// <summary>Messaging system name (e.g., "ais").</summary>
        public const string System = "messaging.system";

        /// <summary>Type of messaging operation.</summary>
        public const string OperationType = "messaging.operation.type";
    }

    /// <summary>
    /// Exception semantic conventions.
    /// </summary>
    public static class Exception
    {
        /// <summary>Full type name of the exception.</summary>
        public const string Type = "exception.type";

        /// <summary>Exception message.</summary>
        public const string Message = "exception.message";

        /// <summary>Exception stack trace.</summary>
        public const string Stacktrace = "exception.stacktrace";

        /// <summary>Whether the exception escaped the scope.</summary>
        public const string Escaped = "exception.escaped";

        /// <summary>Strategy used to handle the exception.</summary>
        public const string HandlingStrategy = "exception.handling_strategy";

        /// <summary>Position in the inner-exception chain; 0 is the outermost exception.</summary>
        public const string Depth = "exception.depth";
    }

    /// <summary>
    /// Error semantic conventions.
    /// </summary>
    public static class Error
    {
        /// <summary>Error type category.</summary>
        public const string Type = "error.type";
    }

    /// <summary>
    /// Event semantic conventions.
    /// </summary>
    public static class Event
    {
        /// <summary>Event name.</summary>
        public const string Name = "event.name";
    }

    /// <summary>
    /// Batch processing semantic conventions.
    /// </summary>
    public static class Batch
    {
        /// <summary>Type of batch operation.</summary>
        public const string Type = "batch.type";

        /// <summary>Number of items in the batch.</summary>
        public const string Size = "batch.size";

        /// <summary>Duration of the batch operation in milliseconds.</summary>
        public const string DurationMs = "batch.duration_ms";
    }

    /// <summary>
    /// AIS domain-specific semantic conventions (custom namespace).
    /// </summary>
    public static class Ais
    {
        /// <summary>AIS message type code (1-27).</summary>
        public const string MessageType = "ais.message.type";

        /// <summary>Maritime Mobile Service Identity.</summary>
        public const string Mmsi = "ais.mmsi";

        /// <summary>Vessel name.</summary>
        public const string VesselName = "ais.vessel.name";

        /// <summary>Vessel call sign.</summary>
        public const string CallSign = "ais.call_sign";

        /// <summary>Ship type code.</summary>
        public const string ShipType = "ais.ship_type";

        /// <summary>Vessel latitude in degrees.</summary>
        public const string PositionLatitude = "ais.position.latitude";

        /// <summary>Vessel longitude in degrees.</summary>
        public const string PositionLongitude = "ais.position.longitude";

        /// <summary>Navigation status code.</summary>
        public const string NavigationStatus = "ais.navigation_status";

        /// <summary>Station identifier.</summary>
        public const string StationId = "ais.station_id";

        /// <summary>Unix timestamp of the message.</summary>
        public const string Timestamp = "ais.timestamp";

        /// <summary>
        /// Storage-related AIS attributes.
        /// </summary>
        public static class Storage
        {
            /// <summary>Number of messages in a batch.</summary>
            public const string MessageCount = "ais.storage.message_count";

            /// <summary>Number of bytes written.</summary>
            public const string Bytes = "ais.storage.bytes";

            /// <summary>Storage blob path.</summary>
            public const string BlobPath = "ais.storage.blob_path";

            /// <summary>Old storage blob path (for rotation events).</summary>
            public const string OldBlobPath = "storage.blob.old_path";

            /// <summary>New storage blob path (for rotation events).</summary>
            public const string NewBlobPath = "storage.blob.new_path";
        }
    }

    /// <summary>
    /// Connection and retry semantic conventions.
    /// </summary>
    public static class Connection
    {
        /// <summary>Connection attempt number.</summary>
        public const string Attempt = "connection.attempt";

        /// <summary>Connection state (e.g., "connected", "disconnected").</summary>
        public const string State = "connection.state";

        /// <summary>Reason for connection state change.</summary>
        public const string StateChangeReason = "connection.state_change.reason";

        /// <summary>Retry delay in milliseconds.</summary>
        public const string RetryDelayMs = "retry.delay_ms";

        /// <summary>Retry attempt number.</summary>
        public const string RetryAttempt = "retry.attempt";
    }
}
