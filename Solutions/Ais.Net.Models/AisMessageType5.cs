// <copyright file="AisMessageType5.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType5(
        uint AisVersion,
        uint EtaMonth,
        uint EtaDay,
        uint EtaHour,
        uint EtaMinute,
        uint Mmsi,
        bool IsDteNotReady,
        uint ImoNumber,
        string CallSign,
        string Destination,
        string VesselName,
        ShipType ShipType,
        uint RepeatIndicator,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        uint Draught10thMetres,
        uint Spare423,
        EpfdFixType PositionFixType) :
        AisMessageBase(MessageType: 5, Mmsi),
        IAisMessageType5,
        IAisIsDteNotReady,
        IAisPositionFixType,
        ICallSign,
        IVesselName,
        IVesselDimensions,
        IRepeatIndicator,
        IShipType;
}
