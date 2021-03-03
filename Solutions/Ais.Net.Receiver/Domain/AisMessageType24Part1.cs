namespace Ais.Net.Receiver.Domain
{
    public class AisMessageType24Part1 : AisMessageBase,
        IAisMessageType24Part1,
        IVesselDimensions,
        IAisMultipartMessage,
        IRepeatIndicator,
        IShipType
    {
        public string CallSign { get; set; }
        public uint MothershipMmsi { get; set; }
        public uint SerialNumber { get; set; }
        public uint Spare162 { get; set; }
        public uint UnitModelCode { get; set; }
        public string VendorIdRev3 { get; set; }
        public string VendorIdRev4 { get; set; }
        public uint DimensionToBow { get; set; }
        public uint DimensionToPort { get; set; }
        public uint DimensionToStarboard { get; set; }
        public uint DimensionToStern { get; set; }
        public uint PartNumber { get; set; }
        public uint RepeatIndicator { get; set; }
        public ShipType ShipType { get; set; }
    }
}