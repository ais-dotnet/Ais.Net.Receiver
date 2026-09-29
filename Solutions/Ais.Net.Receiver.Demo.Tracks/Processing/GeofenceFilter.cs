// <copyright file="GeofenceFilter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text.Json;
using Ais.Net.Receiver.Demo.Tracks.Models;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public class GeofenceFilter
{
    private static readonly JsonSerializerOptions GeoJsonOptions = new()
    {
        Converters = { new GeoJsonConverterFactory() },
    };

    private readonly Geometry _polygon;

    private GeofenceFilter(Geometry polygon)
    {
        _polygon = polygon;
    }

    public static GeofenceFilter LoadFromGeoJson(string path)
    {
        string json = File.ReadAllText(path);

        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("features");
        var geometry = features[0].GetProperty("geometry");
        string geometryJson = geometry.GetRawText();

        var geom = JsonSerializer.Deserialize<Geometry>(geometryJson, GeoJsonOptions)
            ?? throw new InvalidOperationException("Failed to parse geofence geometry");

        return new GeofenceFilter(geom);
    }

    public bool Contains(double longitude, double latitude)
    {
        var point = new Point(longitude, latitude);
        return _polygon.Contains(point);
    }

    public List<VesselTrackPoint> Filter(List<VesselTrackPoint> points) => [.. points.Where(p => Contains(p.Longitude, p.Latitude))];
}
