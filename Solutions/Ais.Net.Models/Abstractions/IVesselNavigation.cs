// <copyright file="IVesselNavigation.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IVesselNavigation
    {
        float? CourseOverGroundDegrees { get; }
        
        Position? Position { get; }
        
        bool PositionAccuracy { get; }
        
        float? SpeedOverGround { get; }
        
        uint TimeStampSecond { get; }
        
        uint TrueHeadingDegrees { get; }
    }
}