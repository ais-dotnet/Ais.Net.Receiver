// <copyright file="IVesselIdentity.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IVesselIdentity
    {
        uint Mmsi { get; }
    }
}