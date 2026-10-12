// <copyright file="VesselTrackPoint.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Tracks.Models;

public record VesselTrackPoint(
    double Longitude,
    double Latitude,
    long Epoch,
    float SpeedOverGround,
    float CourseOverGround);
