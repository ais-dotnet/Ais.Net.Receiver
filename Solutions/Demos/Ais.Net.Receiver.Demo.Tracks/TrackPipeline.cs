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
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The tracks, with fewer than two positions removed.</returns>
    public static async Task<IReadOnlyList<VesselTrack>> BuildAsync(
        INmeaReceiver source,
        GeofenceFilter? geofence = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // retryAttempts: 1 disables the retry pipeline. ReceiverHost defaults to 100 attempts because
        // it is built for a live TCP feed that should reconnect; a recorded source is finite and
        // signals its end by faulting, so the default would replay the whole file 100 more times.
        ReceiverHost receiverHost = new(
            source,
            TimeProvider.System,
            retryPeriodicity: TimeSpan.Zero,
            retryAttempts: 1);
        VesselTrackBuilder trackBuilder = new();
        long currentEpoch = 0;
        int messageCount = 0;

        // The position itself carries no timestamp: it comes from the tag block on the sentence that
        // produced the message. Sentences and messages arrive on the same sequence, so the latest
        // sentence epoch is the one the next message belongs to.
        using IDisposable sentences = receiverHost.Sentences.Subscribe(sentence =>
        {
            long epoch = TagBlockParser.ExtractEpoch(sentence);
            if (epoch > 0)
            {
                Interlocked.Exchange(ref currentEpoch, epoch);
            }
        });

        using IDisposable messages = receiverHost.Messages.Subscribe(message =>
        {
            long epoch = Interlocked.Read(ref currentEpoch);

            if (progress is not null && ++messageCount % 100_000 == 0)
            {
                progress.Report(messageCount);
            }

            if (message is IVesselNavigation navigation)
            {
                trackBuilder.AddNavigation(message.Mmsi, navigation, epoch);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A finite source signals the end of the recording by faulting rather than completing, so
            // this is the normal path for a file or blob replay, not a failure.
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
