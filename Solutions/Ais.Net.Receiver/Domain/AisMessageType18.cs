namespace Ais.Net.Receiver.Domain
{
    public class AisMessageType18 : AisMessageBase,
        IAisMessageType18,
        IVesselNavigation,
        IAisIsAssigned,
        IVesselPosition,
        IRaimFlag,
        IRepeatIndicator
    {
        public Position Position { get; set; }
        public bool CanAcceptMessage22ChannelAssignment { get; set; }
        public bool CanSwitchBands { get; set; }
        public ClassBUnit CsUnit { get; set; }
        public bool HasDisplay { get; set; }
        public bool IsDscAttached { get; set; }
        public ClassBRadioStatusType RadioStatusType { get; set; }
        public int RegionalReserved139 { get; set; }
        public int RegionalReserved38 { get; set; }
        public uint CourseOverGround10thDegrees { get; set; }
        public bool PositionAccuracy { get; set; }
        public uint SpeedOverGroundTenths { get; set; }
        public uint TimeStampSecond { get; set; }
        public uint TrueHeadingDegrees { get; set; }
        public bool IsAssigned { get; set; }
        public bool RaimFlag { get; set; }
        public uint RepeatIndicator { get; set; }
    }
}