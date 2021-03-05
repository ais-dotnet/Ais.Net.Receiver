// <copyright file="AisMessageType18.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType18(
        uint Mmsi,
        Position? Position,
        bool CanAcceptMessage22ChannelAssignment,
        bool CanSwitchBands,
        ClassBUnit CsUnit,
        bool HasDisplay,
        bool IsDscAttached,
        ClassBRadioStatusType RadioStatusType,
        int RegionalReserved139,
        int RegionalReserved38,
        float? CourseOverGroundDegrees,
        bool PositionAccuracy,
        float? SpeedOverGround,
        uint TimeStampSecond,
        uint TrueHeadingDegrees,
        bool IsAssigned,
        bool RaimFlag,
        uint RepeatIndicator) :
            AisMessageBase(MessageType: 18, Mmsi),
            IAisMessageType18,
            IVesselNavigation,
            IAisIsAssigned,
            IRaimFlag,
            IRepeatIndicator;
}