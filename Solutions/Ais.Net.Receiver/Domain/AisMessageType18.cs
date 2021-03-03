namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType18(uint Mmsi) : AisMessageBase(MessageType: 18, Mmsi),
        IAisMessageType18,
        IVesselNavigation,
        IAisIsAssigned,
        IVesselPosition,
        IRaimFlag,
        IRepeatIndicator
    {
        public Position? Position { get; init; }
        public bool CanAcceptMessage22ChannelAssignment { get; init; }
        public bool CanSwitchBands { get; init; }
        public ClassBUnit CsUnit { get; init; }
        public bool HasDisplay { get; init; }
        public bool IsDscAttached { get; init; }
        public ClassBRadioStatusType RadioStatusType { get; init; }
        public int RegionalReserved139 { get; init; }
        public int RegionalReserved38 { get; init; }
        public uint CourseOverGround10thDegrees { get; init; }
        public bool PositionAccuracy { get; init; }
        public uint SpeedOverGroundTenths { get; init; }
        public uint TimeStampSecond { get; init; }
        public uint TrueHeadingDegrees { get; init; }
        public bool IsAssigned { get; init; }
        public bool RaimFlag { get; init; }
        public uint RepeatIndicator { get; init; }
    }
}