// <copyright file="AisMessageType24Part1.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType24Part1(uint Mmsi, string CallSign, string VendorIdRev3, string VendorIdRev4) : AisMessageBase(MessageType: 24, Mmsi),
        IAisMessageType24Part1,
        IVesselDimensions,
        IAisMultipartMessage,
        ICallSign,
        IRepeatIndicator,
        IShipType
    {
        public uint MothershipMmsi { get; init; }
        public uint SerialNumber { get; init; }
        public uint Spare162 { get; init; }
        public uint UnitModelCode { get; init; }
        public uint DimensionToBow { get; init; }
        public uint DimensionToPort { get; init; }
        public uint DimensionToStarboard { get; init; }
        public uint DimensionToStern { get; init; }
        public uint PartNumber { get; init; }
        public uint RepeatIndicator { get; init; }
        public ShipType ShipType { get; init; }
    }
}