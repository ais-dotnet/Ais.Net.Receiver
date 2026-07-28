// <copyright file="ReplayTrackSource.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks;
using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Demo.Tracks.Processing;
using Ais.Net.Receiver.Demo.Tracks.Sources;
using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Options;

using Spectre.IO;

namespace Ais.Net.Receiver.Demo.Visualizer.Replay;

/// <summary>
/// Builds the vessel tracks a replay serves, from either a local NMEA file or a blob the receiver
/// captured, and keeps the result so a page refresh does not reprocess the recording.
/// </summary>
/// <remarks>
/// Reading a recording takes seconds to minutes depending on its size, which is why this is a server
/// concern at all: the browser cannot read an Azure blob without a credential, and re-parsing 90MB of
/// NMEA on every request would make the demo feel broken.
/// </remarks>
public sealed class ReplayTrackSource
{
    private readonly VisualizerOptions options;
    private readonly ILogger<ReplayTrackSource> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    private IReadOnlyList<VesselTrack>? cached;
    private string? cachedLabel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayTrackSource"/> class.
    /// </summary>
    /// <param name="options">The visualiser options.</param>
    /// <param name="logger">The logger.</param>
    public ReplayTrackSource(IOptions<VisualizerOptions> options, ILogger<ReplayTrackSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the tracks for the configured replay source, building them on first use.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The tracks and a label describing where they came from.</returns>
    public async Task<(IReadOnlyList<VesselTrack> Tracks, string Label)> GetAsync(CancellationToken cancellationToken)
    {
        if (this.cached is not null && this.cachedLabel is not null)
        {
            return (this.cached, this.cachedLabel);
        }

        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (this.cached is not null && this.cachedLabel is not null)
            {
                return (this.cached, this.cachedLabel);
            }

            (INmeaReceiver source, string label) = this.CreateSource();

            GeofenceFilter? geofence = string.IsNullOrWhiteSpace(this.options.Replay.GeofencePath)
                ? null
                : GeofenceFilter.LoadFromGeoJson(this.options.Replay.GeofencePath);

            this.logger.LogInformation("Building replay tracks from {Source}", label);

            await using (source.ConfigureAwait(false))
            {
                this.cached = await TrackPipeline.BuildAsync(
                    source,
                    geofence,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            this.cachedLabel = label;

            this.logger.LogInformation(
                "Replay ready: {VesselCount} vessels, {PositionCount} positions",
                this.cached.Count,
                this.cached.Sum(t => t.Positions.Count));

            return (this.cached, label);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private (INmeaReceiver Source, string Label) CreateSource()
    {
        ReplayOptions replay = this.options.Replay;

        if (!string.IsNullOrWhiteSpace(replay.BlobPath))
        {
            if (string.IsNullOrWhiteSpace(replay.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Visualizer:Replay:BlobPath needs Visualizer:Replay:ConnectionString (or Storage:ConnectionString) to read the capture.");
            }

            return (
                new BlobNmeaReceiver(replay.ConnectionString, replay.ContainerName, replay.BlobPath),
                $"{replay.ContainerName}/{replay.BlobPath}");
        }

        if (!string.IsNullOrWhiteSpace(replay.FilePath))
        {
            if (!System.IO.File.Exists(replay.FilePath))
            {
                throw new FileNotFoundException($"Replay file not found: {replay.FilePath}", replay.FilePath);
            }

            return (
                new FileStreamNmeaReceiver(new FileSystem(), new FilePath(replay.FilePath)),
                replay.FilePath);
        }

        throw new InvalidOperationException(
            "Replay needs either Visualizer:Replay:FilePath or Visualizer:Replay:BlobPath.");
    }
}
