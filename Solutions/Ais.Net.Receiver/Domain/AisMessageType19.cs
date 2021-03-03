namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType19(uint Mmsi, string ShipName) : AisMessageBase(MessageType: 19, Mmsi),
        IVesselNavigation,
        IVesselDimensions,
        IAisIsAssigned,
        IAisMessageType19,
        IVesselPosition,
        IRaimFlag,
        IRepeatIndicator,
        IShipType
    {
        public Position? Position { get; init; }
        public bool IsDteNotReady { get; init; }
        public EpfdFixType PositionFixType { get; init; }
        public int RegionalReserved139 { get; init; }
        public int RegionalReserved38 { get; init; }
        public uint Spare308 { get; init; }
        public uint CourseOverGround10thDegrees { get; init; }
        public bool PositionAccuracy { get; init; }
        public uint SpeedOverGroundTenths { get; init; }
        public uint TimeStampSecond { get; init; }
        public uint TrueHeadingDegrees { get; init; }
        public uint DimensionToBow { get; init; }
        public uint DimensionToPort { get; init; }
        public uint DimensionToStarboard { get; init; }
        public uint DimensionToStern { get; init; }
        public bool IsAssigned { get; init; }
        public bool RaimFlag { get; init; }
        public uint RepeatIndicator { get; init; }
        public ShipType ShipType { get; init; }
    }
}