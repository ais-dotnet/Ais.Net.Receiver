// <copyright file="TrackPipeline.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Demo.Tracks.Processing;
using Ais.Net.Receiver.Receiver;

namespace Ais.Net.Receiver.Demo.Tracks;

/// <summary>
/// Turns a stream of recorded NMEA into per-vessel tracks ready for the visualiser: decode, group by
/// vessel, optionally clip to a geofence, then downsample.
/// </summary>
/// <remarks>
/// This is the POC's <c>Program.cs</c> as a reusable operation, so the visualiser can generate tracks
/// on demand for whichever source a replay names instead of everything being driven from a console
/// entry point with hardcoded paths.
/// </remarks>
public static class TrackPipeline
{
    /// <summary>
    /// Builds vessel tracks from a recorded NMEA source.
    /// </summary>
    /// <param name="source">The NMEA source - a file or a captured blob.</param>
    /// <param name="geofence">Optional geofence; positions outside it are discarded.</param>
    /// <param name="progress">Optional callback reporting messages processed, for long files.</param>
    /// <param name="onStreamFault">
    /// Invoked when the source faults after messages have already decoded. The build still returns
    /// what it read, but the caller must know the result may be truncated - serving or caching it as
    /// a complete replay would silently lose most of the recording.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The tracks, with fewer than two positions removed.</returns>
    public static async Task<IReadOnlyList<VesselTrack>> BuildAsync(
        INmeaReceiver source,
        GeofenceFilter? geofence = null,
        IProgress<int>? progress = null,
        Action<Exception>? onStreamFault = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // retryAttempts: 1 disables the retry pipeline. ReceiverHost defaults to 100 attempts because
        // it is built for a live TCP feed that should reconnect; a recorded source is finite, so the
        // default would replay the whole recording up to 100 more times.
        ReceiverHost receiverHost = new(
            source,
            TimeProvider.System,
            retryPeriodicity: TimeSpan.Zero,
            retryAttempts: 1);
        VesselTrackBuilder trackBuilder = new();
        int messageCount = 0;

        // The receiver already parses each sentence's tag block and pairs the result with the decoded
        // message on the Metadata stream, so the position's time comes from there rather than this
        // pipeline re-parsing tag blocks out of the sentence text itself.
        using IDisposable subscription = receiverHost.Metadata.Subscribe(item =>
        {
            messageCount++;

            if (progress is not null && messageCount % 100_000 == 0)
            {
                progress.Report(messageCount);
            }

            IAisMessage message = item.Message;

            if (message is IVesselNavigation navigation)
            {
                trackBuilder.AddNavigation(message.Mmsi, navigation, item.UnixTimestamp);
            }

            if (message is IVesselName vesselName)
            {
                trackBuilder.AddName(message.Mmsi, vesselName);
            }

            if (message is IShipType shipType)
            {
                trackBuilder.AddShipType(message.Mmsi, shipType);
            }
        });

        try
        {
            await receiverHost.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && messageCount > 0)
        {
            // A network-style source signals the end of a finite recording by faulting once the far
            // end goes away, which is normal completion here. A fault before any message decoded is
            // different - a missing blob, a bad connection string - and swallowing it would quietly
            // produce (and let the caller cache) an empty replay, so that one propagates. Faults
            // after data are reported through the callback because for a file or blob they mean a
            // mid-stream read failure, and the caller must not treat the partial result as complete.
            onStreamFault?.Invoke(ex);
        }

        List<VesselTrack> tracks = [.. trackBuilder.GetTracks()];

        foreach (VesselTrack track in tracks)
        {
            if (geofence is not null)
            {
                track.Positions = geofence.Filter(track.Positions);
            }

            if (track.Positions.Count > 2)
            {
                track.Positions = TrackDownsampler.Downsample(track.Positions);
            }
        }

        // A single position draws neither a track nor a movement, and the writer would skip it anyway.
        return [.. tracks.Where(t => t.Positions.Count >= 2)];
    }
}
