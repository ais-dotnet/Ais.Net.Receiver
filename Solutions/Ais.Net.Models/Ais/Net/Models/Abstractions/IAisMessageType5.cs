// <copyright file="IAisMessageType5.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IAisMessageType5
    {
        uint AisVersion { get; }

        string Destination { get; }

        uint Draught10thMetres { get; }

        uint EtaMonth { get; }

        uint EtaDay { get; }

        uint EtaHour { get; }

        uint EtaMinute { get; }

        uint ImoNumber { get; }

        uint Spare423 { get; }
    }
}