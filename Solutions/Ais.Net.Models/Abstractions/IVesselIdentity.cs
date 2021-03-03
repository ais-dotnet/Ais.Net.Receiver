// <copyright file="IVesselIdentity.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IVesselIdentity
    {
        uint Mmsi { get; }
    }
}