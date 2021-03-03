namespace Ais.Net.Receiver.Domain
{
    public class AisMessageType24Part0 : AisMessageBase,
        IAisMultipartMessage,
        IRepeatIndicator,
        IAisMessageType24Part0
    {
        public uint Spare160 { get; set; }
        public uint PartNumber { get; set; }
        public uint RepeatIndicator { get; set; }
    }
}