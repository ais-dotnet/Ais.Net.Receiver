// <copyright file="GeofenceFilter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using Ais.Net.Receiver.Demo.Tracks.Models;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public class GeofenceFilter
{
    private readonly Geometry _polygon;

    private GeofenceFilter(Geometry polygon)
    {
        _polygon = polygon;
    }

    public static GeofenceFilter LoadFromGeoJson(string path)
    {
        string json = File.ReadAllText(path);

        var options = new JsonSerializerOptions();
        options.Converters.Add(new GeoJsonConverterFactory());

        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("features");
        var geometry = features[0].GetProperty("geometry");
        string geometryJson = geometry.GetRawText();

        var geom = JsonSerializer.Deserialize<Geometry>(geometryJson, options)
            ?? throw new InvalidOperationException("Failed to parse geofence geometry");

        return new GeofenceFilter(geom);
    }

    public bool Contains(double longitude, double latitude)
    {
        var point = new Point(longitude, latitude);
        return _polygon.Contains(point);
    }

    public List<VesselTrackPoint> Filter(List<VesselTrackPoint> points)
    {
        return points.Where(p => Contains(p.Longitude, p.Latitude)).ToList();
    }

    public double[][] GetPolygonCoordinates()
    {
        var coords = _polygon.Coordinates;
        return coords.Select(c => new[] { c.X, c.Y }).ToArray();
    }
}
