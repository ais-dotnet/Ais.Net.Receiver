namespace Ais.Net.Receiver.Domain
{
    public class AisMessageType1Through3 : AisMessageBase,
        IAisMessageType1to3,
        IVesselPosition,
        IVesselNavigation,
        IRaimFlag,
        IRepeatIndicator
    {
        public Position Position { get; set; }
        public ManoeuvreIndicator ManoeuvreIndicator { get; set; }
        public NavigationStatus NavigationStatus { get; set; }
        public uint RadioSlotTimeout { get; set; }
        public uint RadioSubMessage { get; set; }
        public RadioSyncState RadioSyncState { get; set; }
        public int RateOfTurn { get; set; }
        public uint SpareBits145 { get; set; }
        public uint CourseOverGround10thDegrees { get; set; }
        public bool PositionAccuracy { get; set; }
        public uint SpeedOverGroundTenths { get; set; }
        public uint TimeStampSecond { get; set; }
        public uint TrueHeadingDegrees { get; set; }
        public bool RaimFlag { get; set; }
        public uint RepeatIndicator { get; set; }
    }
}