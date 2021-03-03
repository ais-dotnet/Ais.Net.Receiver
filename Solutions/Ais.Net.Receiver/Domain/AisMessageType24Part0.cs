namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType24Part0(
        uint Mmsi,
        uint Spare160,
        uint PartNumber,
        uint RepeatIndicator) :
            AisMessageBase(MessageType: 24, Mmsi),
            IAisMultipartMessage,
            IRepeatIndicator,
            IAisMessageType24Part0;
}