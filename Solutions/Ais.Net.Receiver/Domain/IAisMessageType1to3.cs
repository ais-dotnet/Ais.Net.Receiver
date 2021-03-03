namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType1to3
    {
        ManoeuvreIndicator ManoeuvreIndicator { get; set; }
        NavigationStatus NavigationStatus { get; set; }
        uint RadioSlotTimeout { get; set; }
        uint RadioSubMessage { get; set; }
        RadioSyncState RadioSyncState { get; set; }
        int RateOfTurn { get; set; }
        uint SpareBits145 { get; set; }
    }
}