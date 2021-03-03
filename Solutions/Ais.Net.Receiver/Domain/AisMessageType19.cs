namespace Ais.Net.Receiver.Domain
{
    public class AisMessageType19 : AisMessageBase,
        IVesselNavigation,
        IVesselDimensions,
        IAisIsAssigned,
        IAisMessageType19,
        IVesselPosition,
        IRaimFlag,
        IRepeatIndicator,
        IShipType
    {
        public Position Position { get; set; }
        public bool IsDteNotReady { get; set; }
        public EpfdFixType PositionFixType { get; set; }
        public int RegionalReserved139 { get; set; }
        public int RegionalReserved38 { get; set; }
        public string ShipName { get; set; }
        public uint Spare308 { get; set; }
        public uint CourseOverGround10thDegrees { get; set; }
        public bool PositionAccuracy { get; set; }
        public uint SpeedOverGroundTenths { get; set; }
        public uint TimeStampSecond { get; set; }
        public uint TrueHeadingDegrees { get; set; }
        public uint DimensionToBow { get; set; }
        public uint DimensionToPort { get; set; }
        public uint DimensionToStarboard { get; set; }
        public uint DimensionToStern { get; set; }
        public bool IsAssigned { get; set; }
        public bool RaimFlag { get; set; }
        public uint RepeatIndicator { get; set; }
        public ShipType ShipType { get; set; }
    }
}