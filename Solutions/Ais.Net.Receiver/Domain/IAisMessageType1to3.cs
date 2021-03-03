namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType1to3
    {
        ManoeuvreIndicator ManoeuvreIndicator { get; }
        NavigationStatus NavigationStatus { get; }
        uint RadioSlotTimeout { get; }
        uint RadioSubMessage { get; }
        RadioSyncState RadioSyncState { get; }
        int RateOfTurn { get; }
        uint SpareBits145 { get; }
    }
}