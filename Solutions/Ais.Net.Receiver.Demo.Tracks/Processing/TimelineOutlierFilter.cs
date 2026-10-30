// <copyright file="TimelineOutlierFilter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Models;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

/// <summary>
/// Removes positions whose timestamps sit far outside the bulk of a recording.
/// </summary>
/// <remarks>
/// A base station with a wrong clock can stamp its messages a day away from everything else. The
/// replay timeline runs from the earliest position to the latest, so a few such messages would leave
/// most of the slider empty. Positions are grouped into runs separated by long silences, and only the
/// run holding the most positions is kept.
/// </remarks>
public static class TimelineOutlierFilter
{
    // Long enough that a receiver outage does not split a real recording, and short enough to catch
    // a clock that is a day out.
    private const long MaxGapSeconds = 6 * 60 * 60;

    public static void Apply(IReadOnlyCollection<VesselTrack> tracks)
    {
        long[] epochs = [.. tracks.SelectMany(t => t.Positions).Select(p => p.Epoch)];
        if (epochs.Length == 0)
            return;

        Array.Sort(epochs);

        int bestStart = 0;
        int bestLength = 0;
        int runStart = 0;

        for (int i = 1; i <= epochs.Length; i++)
        {
            if (i < epochs.Length && epochs[i] - epochs[i - 1] <= MaxGapSeconds)
                continue;

            if (i - runStart > bestLength)
            {
                bestStart = runStart;
                bestLength = i - runStart;
            }

            runStart = i;
        }

        if (bestLength == epochs.Length)
            return;

        long first = epochs[bestStart];
        long last = epochs[bestStart + bestLength - 1];

        foreach (VesselTrack track in tracks)
        {
            track.Positions.RemoveAll(p => p.Epoch < first || p.Epoch > last);
        }
    }
}
