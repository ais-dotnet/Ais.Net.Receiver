// <copyright file="Coordinates.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Reactor.EventData.Ais
{
    using Nuqleon.DataModel;

    /// <summary>
    /// Represents a position on the surface of a geoid (typically GPS's WGS84) in polar
    /// coordinates.
    /// </summary>
    public class Coordinates
    {
        /// <summary>
        /// Gets or sets the latitude (north-south position) in degrees.
        /// </summary>
        [Mapping("ais://coordinates/lat")]
        public double Latitude { get; set; }

        /// <summary>
        /// Gets or sets the longitude (east-west position) in degrees.
        /// </summary>
        [Mapping("ais://coordinates/long")]
        public double Longitude { get; set; }
    }
}