namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType18
    {
        bool CanAcceptMessage22ChannelAssignment { get; set; }
        bool CanSwitchBands { get; set; }
        ClassBUnit CsUnit { get; set; }
        bool HasDisplay { get; set; }
        bool IsDscAttached { get; set; }
        ClassBRadioStatusType RadioStatusType { get; set; }
        int RegionalReserved139 { get; set; }
        int RegionalReserved38 { get; set; }
    }
}