// <copyright file="VesselTrackBuilder.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Demo.Tracks.Models;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public class VesselTrackBuilder
{
    private readonly Dictionary<uint, VesselTrack> _tracks = new();
    private readonly Lock _lock = new();

    public void AddNavigation(uint mmsi, IVesselNavigation navigation, long epoch)
    {
        if (navigation.Position is null) return;

        double lat = navigation.Position?.Latitude ?? 0;
        double lon = navigation.Position?.Longitude ?? 0;

        // Filter out invalid positions
        if (lat is 0 or > 90 or < -90 || lon is 0 or > 180 or < -180)
            return;

        if (epoch <= 0)
            return;

        var point = new VesselTrackPoint(
            lon,
            lat,
            epoch,
            navigation.SpeedOverGround ?? 0,
            navigation.CourseOverGround ?? 0);

        lock (_lock)
        {
            GetOrCreate(mmsi).Positions.Add(point);
        }
    }

    public void AddName(uint mmsi, IVesselName vesselName)
    {
        string name = vesselName.VesselName.CleanVesselName();
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_lock)
        {
            VesselTrack track = GetOrCreate(mmsi);

            if (string.IsNullOrEmpty(track.Metadata.Name))
            {
                track.Metadata.Name = name;
            }
        }
    }

    public void AddShipType(uint mmsi, IShipType shipTypeInfo)
    {
        var (category, color) = ShipTypeColors.GetCategoryAndColor(shipTypeInfo.ShipType);

        lock (_lock)
        {
            VesselTrack track = GetOrCreate(mmsi);

            if (string.IsNullOrEmpty(track.Metadata.ShipType))
            {
                track.Metadata.ShipType = shipTypeInfo.ShipType.ToString();
                track.Metadata.ShipTypeCategory = category;
                track.Metadata.Color = color;
            }
        }
    }

    public IReadOnlyCollection<VesselTrack> GetTracks() => _tracks.Values;

    // Callers hold _lock; AIS splits a vessel across message types, so whichever arrives first
    // creates the track the rest enrich.
    private VesselTrack GetOrCreate(uint mmsi)
    {
        if (!_tracks.TryGetValue(mmsi, out VesselTrack? track))
        {
            track = new VesselTrack { Mmsi = mmsi };
            _tracks[mmsi] = track;
        }

        return track;
    }
}
