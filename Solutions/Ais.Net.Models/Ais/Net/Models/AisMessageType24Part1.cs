// <copyright file="AisMessageType24Part1.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType24Part1(
        string CallSign,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        uint Mmsi,
        uint MothershipMmsi,
        uint PartNumber,
        uint RepeatIndicator,
        uint SerialNumber,
        uint Spare162,
        ShipType ShipType,
        uint UnitModelCode,
        string VendorIdRev3,
        string VendorIdRev4,
        long? UnixTimestamp) :
            AisMessageBase(MessageType: 24, Mmsi, UnixTimestamp),
            IAisMessageType24Part1,
            IAisMultipartMessage,
            ICallSign,
            IRepeatIndicator,
            IShipType,
            IVesselDimensions;
}