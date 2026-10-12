// <copyright file="VesselTrack.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Tracks.Models;

public class VesselTrack
{
    public uint Mmsi { get; set; }
    public VesselMetadata Metadata { get; set; } = new();
    public List<VesselTrackPoint> Positions { get; set; } = [];
}
