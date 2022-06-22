// <copyright file="AisMessageType18.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType27(
        float? CourseOverGroundDegrees,
        bool NotGnssPosition,
        uint Mmsi,
        NavigationStatus NavigationStatus,
        Position? Position,
        bool PositionAccuracy,
        bool RaimFlag,
        uint RepeatIndicator,
        float? SpeedOverGround,
        long? UnixTimestamp) :
            AisMessageBase(MessageType: 27, Mmsi, UnixTimestamp),
            IAisMessageType27,
            IRaimFlag,
            IRepeatIndicator;
}