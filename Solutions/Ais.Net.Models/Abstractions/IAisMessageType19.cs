// <copyright file="IAisMessageType19.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IAisMessageType19
    {
        int RegionalReserved139 { get; }

        int RegionalReserved38 { get; }

        string ShipName { get; }

        uint Spare308 { get; }
    }
}