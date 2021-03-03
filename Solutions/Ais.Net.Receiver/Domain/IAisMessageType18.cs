// <copyright file="IAisMessageType18.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType18
    {
        bool CanAcceptMessage22ChannelAssignment { get; }
        bool CanSwitchBands { get; }
        ClassBUnit CsUnit { get; }
        bool HasDisplay { get; }
        bool IsDscAttached { get; }
        ClassBRadioStatusType RadioStatusType { get; }
        int RegionalReserved139 { get; }
        int RegionalReserved38 { get; }
    }
}