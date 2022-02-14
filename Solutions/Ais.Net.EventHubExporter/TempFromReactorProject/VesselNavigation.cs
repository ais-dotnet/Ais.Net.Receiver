// <copyright file="VesselNavigation.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Reactor.EventData.Ais
{
    using Nuqleon.DataModel;

    /// <summary>
    /// AIS vessel navigation information.
    /// </summary>
    public class VesselNavigation
    {
        /// <summary>
        /// Gets or sets the vessel's position.
        /// </summary>
        [Mapping("ais://vessel_nav/position")]
        public Coordinates Position { get; set; }

        /// <summary>
        /// Gets or sets the direction of travel of the vessel in degrees.
        /// </summary>
        [Mapping("ais://vessel_nav/cogdegrees")]
        public float? CourseOverGroundDegrees { get; set; }
    }
}