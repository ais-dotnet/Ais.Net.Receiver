// <copyright file="AisMessageType1Through3.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

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
        float? CourseOverGroundDegrees,
        bool PositionAccuracy,
        float? SpeedOverGround,
        uint TimeStampSecond,
        uint TrueHeadingDegrees,
        bool RaimFlag,
        uint RepeatIndicator) :
            AisMessageBase(MessageType, Mmsi),
            IAisMessageType1to3,
            IVesselNavigation,
            IRaimFlag,
            IRepeatIndicator;
}