// <copyright file="VisualizerOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Visualizer;

/// <summary>
/// Which data the visualiser shows and where it comes from.
/// </summary>
public sealed class VisualizerOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Visualizer";

    /// <summary>
    /// Gets or sets the data source. Live is the default: the AppHost runs the receiver and a NATS
    /// broker, so a plain <c>dotnet run</c> shows vessels moving now rather than a recording.
    /// </summary>
    public VisualizerSource Source { get; set; } = VisualizerSource.Live;

    /// <summary>Gets or sets the replay settings, used when <see cref="Source"/> is Replay.</summary>
    public ReplayOptions Replay { get; set; } = new();

    /// <summary>
    /// Gets or sets the NATS subject the receiver publishes decoded messages on, and which the page
    /// subscribes to directly.
    /// </summary>
    public string MessageSubject { get; set; } = "ais.messages";

    /// <summary>
    /// Gets or sets the websocket URL the browser connects to. The AppHost supplies this because the
    /// port is assigned at run time.
    /// </summary>
    public string? NatsWebSocketUrl { get; set; }

    /// <summary>Gets or sets the basemap style URL.</summary>
    public string BasemapStyle { get; set; } = "https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json";

    /// <summary>Gets or sets how long a vessel may go unheard before it is dropped from the live view.</summary>
    public TimeSpan VesselInactivityTimeout { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Where the visualiser gets vessel data.</summary>
public enum VisualizerSource
{
    /// <summary>Live positions from the receiver, over NATS.</summary>
    Live,

    /// <summary>A recorded NMEA file or captured blob, played back on a timeline.</summary>
    Replay,
}

/// <summary>Settings for replaying a recording.</summary>
public sealed class ReplayOptions
{
    /// <summary>Gets or sets a local path to an NMEA (.nm4) file.</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets the path of a captured blob, in the layout the receiver writes:
    /// <c>raw/yyyy/MM/dd/yyyyMMddTHH.nm4</c>. Requires <see cref="ConnectionString"/>.
    /// </summary>
    public string? BlobPath { get; set; }

    /// <summary>
    /// Gets or sets the storage connection string for <see cref="BlobPath"/>. Defaults to the
    /// receiver's own <c>Storage:ConnectionString</c>, so replaying what Azurite captured needs no
    /// extra configuration.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the blob container holding the capture.</summary>
    public string ContainerName { get; set; } = "nmea-ais-dev";

    /// <summary>Gets or sets an optional GeoJSON geofence; positions outside it are discarded.</summary>
    public string? GeofencePath { get; set; }
}
