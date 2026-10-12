// <copyright file="TrackDownsampler.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Models;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public static class TrackDownsampler
{
    private const double MinDistanceDegrees = 0.001;
    private const long MinTimeGapSeconds = 300;

    public static List<VesselTrackPoint> Downsample(List<VesselTrackPoint> points)
    {
        if (points.Count <= 2)
            return points;

        // Sort by epoch
        points.Sort((a, b) => a.Epoch.CompareTo(b.Epoch));

        var result = new List<VesselTrackPoint> { points[0] };

        for (int i = 1; i < points.Count; i++)
        {
            var prev = result[^1];
            var curr = points[i];

            double dLon = Math.Abs(curr.Longitude - prev.Longitude);
            double dLat = Math.Abs(curr.Latitude - prev.Latitude);
            long dTime = curr.Epoch - prev.Epoch;

            if (dLon > MinDistanceDegrees || dLat > MinDistanceDegrees || dTime > MinTimeGapSeconds)
            {
                result.Add(curr);
            }
        }

        // Always keep the last point
        if (result[^1] != points[^1])
        {
            result.Add(points[^1]);
        }

        return result;
    }
}
