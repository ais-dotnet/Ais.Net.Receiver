// <copyright file="IVesselIdentity.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public interface IVesselIdentity
    {
        uint Mmsi { get; }
    }
}