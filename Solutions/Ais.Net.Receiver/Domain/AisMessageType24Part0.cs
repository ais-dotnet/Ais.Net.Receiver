namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType24Part0(uint Mmsi) : AisMessageBase(MessageType: 24, Mmsi),
        IAisMultipartMessage,
        IRepeatIndicator,
        IAisMessageType24Part0
    {
        public uint Spare160 { get; init; }
        public uint PartNumber { get; init; }
        public uint RepeatIndicator { get; init; }
    }
}