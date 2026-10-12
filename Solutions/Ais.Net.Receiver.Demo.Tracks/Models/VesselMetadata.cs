// <copyright file="VesselMetadata.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Tracks.Models;

public class VesselMetadata
{
    public string Name { get; set; } = string.Empty;
    public string ShipType { get; set; } = string.Empty;
    public string ShipTypeCategory { get; set; } = string.Empty;
    public int[] Color { get; set; } = [150, 249, 161];
}
