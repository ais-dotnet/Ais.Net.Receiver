// <copyright file="VesselPositionUpdate.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Visualizer.Live;

/// <summary>
/// One vessel's current position, shaped for the map rather than for AIS fidelity.
/// </summary>
/// <remarks>
/// The field names and the colour triple deliberately match the per-vessel shape the replay pipeline
/// emits, so the frontend's layers read live and recorded vessels the same way.
/// </remarks>
/// <param name="Mmsi">The vessel's MMSI.</param>
/// <param name="Name">The vessel name, or a placeholder until a static message supplies one.</param>
/// <param name="ShipType">The raw ship type, when known.</param>
/// <param name="ShipTypeCategory">The category the colour is derived from.</param>
/// <param name="Color">The RGB triple for this ship type category.</param>
/// <param name="Coordinates">Longitude then latitude, matching GeoJSON and deck.gl ordering.</param>
/// <param name="Speed">Speed over ground, in knots.</param>
/// <param name="Course">Course over ground, in degrees.</param>
/// <param name="Timestamp">Unix seconds when this update was published.</param>
public readonly record struct VesselPositionUpdate(
    uint Mmsi,
    string Name,
    string ShipType,
    string ShipTypeCategory,
    int[] Color,
    double[] Coordinates,
    float Speed,
    float Course,
    long Timestamp);
