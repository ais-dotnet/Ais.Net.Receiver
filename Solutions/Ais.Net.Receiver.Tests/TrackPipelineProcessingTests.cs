// <copyright file="TrackPipelineProcessingTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Demo.Tracks.Processing;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Covers the pure functions the visualiser demo's track pipeline is built from. They arrived with
/// the proof of concept untested, and they decide what the map actually shows: which points survive
/// downsampling and which fall outside the geofence. (Position timestamps come from the receiver's
/// own tag-block parsing, covered by <see cref="TrackPipelineTests"/>.)
/// </summary>
[TestClass]
public class TrackPipelineProcessingTests
{
    [TestMethod]
    public void Downsample_KeepsPointsThatMovedFarEnough()
    {
        List<VesselTrackPoint> points =
        [
            new(10.0, 58.0, 1000, 12, 90),
            new(10.5, 58.0, 1010, 12, 90),
            new(11.0, 58.0, 1020, 12, 90),
        ];

        TrackDownsampler.Downsample(points).Count.ShouldBe(3);
    }

    [TestMethod]
    public void Downsample_DropsPointsThatBarelyMoved()
    {
        // A moored vessel reports repeatedly from the same spot. Those points cost memory and draw
        // nothing, so everything between the first and last is dropped.
        List<VesselTrackPoint> points =
        [
            new(10.0, 58.0, 1000, 0, 0),
            new(10.00001, 58.00001, 1010, 0, 0),
            new(10.00002, 58.00002, 1020, 0, 0),
            new(10.00003, 58.00003, 1030, 0, 0),
        ];

        List<VesselTrackPoint> result = TrackDownsampler.Downsample(points);

        result.Count.ShouldBe(2);
        result[0].Epoch.ShouldBe(1000);
        result[^1].Epoch.ShouldBe(1030);
    }

    [TestMethod]
    public void Downsample_KeepsAStationaryPointAfterALongGap()
    {
        // Same position, but five minutes later: keeping it is what stops the trail interpolating
        // straight through a gap the vessel was not actually moving during.
        List<VesselTrackPoint> points =
        [
            new(10.0, 58.0, 1000, 0, 0),
            new(10.0, 58.0, 1400, 0, 0),
        ];

        TrackDownsampler.Downsample(points).Count.ShouldBe(2);
    }

    [TestMethod]
    public void Downsample_SortsByTime()
    {
        List<VesselTrackPoint> points =
        [
            new(11.0, 58.0, 1020, 12, 90),
            new(10.0, 58.0, 1000, 12, 90),
            new(10.5, 58.0, 1010, 12, 90),
        ];

        List<VesselTrackPoint> result = TrackDownsampler.Downsample(points);

        result.Select(p => p.Epoch).ShouldBe([1000, 1010, 1020]);
    }

    [TestMethod]
    public void Geofence_KeepsOnlyPointsInsideThePolygon()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "box.json");

        // A square from 10,58 to 11,59.
        File.WriteAllText(
            path,
            """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": {},
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [[[10,58],[11,58],[11,59],[10,59],[10,58]]]
                  }
                }
              ]
            }
            """);

        GeofenceFilter filter = GeofenceFilter.LoadFromGeoJson(path);

        filter.Contains(10.5, 58.5).ShouldBeTrue();
        filter.Contains(12.0, 58.5).ShouldBeFalse();

        List<VesselTrackPoint> filtered = filter.Filter(
        [
            new(10.5, 58.5, 1000, 0, 0),
            new(12.0, 58.5, 1010, 0, 0),
            new(10.2, 58.2, 1020, 0, 0),
        ]);

        filtered.Count.ShouldBe(2);
        filtered.ShouldAllBe(p => p.Longitude < 11);
    }
}
