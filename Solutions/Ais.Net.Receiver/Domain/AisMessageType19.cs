// <copyright file="AisMessageType19.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType19(
        uint Mmsi,
        string ShipName,
        Position? Position,
        bool IsDteNotReady,
        EpfdFixType PositionFixType,
        int RegionalReserved139,
        int RegionalReserved38,
        uint Spare308,
        uint CourseOverGround10thDegrees,
        bool PositionAccuracy,
        uint SpeedOverGroundTenths,
        uint TimeStampSecond,
        uint TrueHeadingDegrees,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        bool IsAssigned,
        bool RaimFlag,
        uint RepeatIndicator,
        ShipType ShipType) :
            AisMessageBase(MessageType: 19, Mmsi),
            IVesselNavigation,
            IVesselDimensions,
            IAisIsAssigned,
            IAisMessageType19,
            IVesselPosition,
            IRaimFlag,
            IRepeatIndicator,
            IShipType;
}