namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType1Through3(int MessageType, uint Mmsi) : AisMessageBase(MessageType, Mmsi),
        IAisMessageType1to3,
        IVesselPosition,
        IVesselNavigation,
        IRaimFlag,
        IRepeatIndicator
    {
        public Position? Position { get; init; }
        public ManoeuvreIndicator ManoeuvreIndicator { get; init; }
        public NavigationStatus NavigationStatus { get; init; }
        public uint RadioSlotTimeout { get; init; }
        public uint RadioSubMessage { get; init; }
        public RadioSyncState RadioSyncState { get; init; }
        public int RateOfTurn { get; init; }
        public uint SpareBits145 { get; init; }
        public uint CourseOverGround10thDegrees { get; init; }
        public bool PositionAccuracy { get; init; }
        public uint SpeedOverGroundTenths { get; init; }
        public uint TimeStampSecond { get; init; }
        public uint TrueHeadingDegrees { get; init; }
        public bool RaimFlag { get; init; }
        public uint RepeatIndicator { get; init; }
    }
}