// <copyright file="AisMessageType19.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType19(
        float? CourseOverGroundDegrees,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        bool IsAssigned,
        bool IsDteNotReady,
        uint Mmsi,
        Position? Position,
        bool PositionAccuracy,
        EpfdFixType PositionFixType,
        bool RaimFlag,
        int RegionalReserved139,
        int RegionalReserved38,
        uint RepeatIndicator,
        string ShipName,
        ShipType ShipType,
        uint Spare308,
        float? SpeedOverGround,
        uint TimeStampSecond,
        uint TrueHeadingDegrees) :
            AisMessageBase(MessageType: 19, Mmsi),
            IAisMessageType19,
            IAisIsAssigned,
            IAisIsDteNotReady,
            IAisPositionFixType,
            IRaimFlag,
            IRepeatIndicator,
            IShipType,
            IVesselDimensions,
            IVesselNavigation;
}