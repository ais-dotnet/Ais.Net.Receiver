// <copyright file="AisMessageType1Through3.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType1Through3(
        int MessageType,
        uint Mmsi,
        Position? Position,
        ManoeuvreIndicator ManoeuvreIndicator,
        NavigationStatus NavigationStatus,
        uint RadioSlotTimeout,
        uint RadioSubMessage,
        RadioSyncState RadioSyncState,
        int RateOfTurn,
        uint SpareBits145,
        uint CourseOverGround10thDegrees,
        bool PositionAccuracy,
        uint SpeedOverGroundTenths,
        uint TimeStampSecond,
        uint TrueHeadingDegrees,
        bool RaimFlag,
        uint RepeatIndicator) :
            AisMessageBase(MessageType, Mmsi),
            IAisMessageType1to3,
            IVesselPosition,
            IVesselNavigation,
            IRaimFlag,
            IRepeatIndicator;
}