// <copyright file="IVesselNavigation.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public interface IVesselNavigation
    {
        uint CourseOverGround10thDegrees { get; }
        bool PositionAccuracy { get; }
        uint SpeedOverGroundTenths { get; }
        uint TimeStampSecond { get; }
        uint TrueHeadingDegrees { get; }
    }
}