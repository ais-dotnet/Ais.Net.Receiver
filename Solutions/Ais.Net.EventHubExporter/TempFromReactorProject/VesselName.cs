// <copyright file="VesselName.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Reactor.EventData.Ais
{
    using Nuqleon.DataModel;

    /// <summary>
    /// AIS vessel name information.
    /// </summary>
    public class VesselName
    {
        /// <summary>
        /// Gets or sets the vessel name.
        /// </summary>
        [Mapping("ais://vessel_name/name")]
        public string Name { get; set; }
    }
}