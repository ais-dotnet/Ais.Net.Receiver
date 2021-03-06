// <copyright file="AisMessageType5.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType5(
        uint AisVersion,
        string CallSign,
        string Destination,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        uint Draught10thMetres,
        uint EtaDay,
        uint EtaHour,
        uint EtaMinute,
        uint EtaMonth,
        bool IsDteNotReady,
        uint ImoNumber,
        uint Mmsi,
        EpfdFixType PositionFixType,
        uint RepeatIndicator,
        ShipType ShipType,
        uint Spare423,
        string VesselName) :
            AisMessageBase(MessageType: 5, Mmsi),
            IAisMessageType5,
            IAisIsDteNotReady,
            IAisPositionFixType,
            ICallSign,
            IRepeatIndicator,
            IShipType,
            IVesselDimensions,
            IVesselName;
}
