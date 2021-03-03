namespace Ais.Net.Receiver.Domain
{
    public interface IAisPositionFixType
    {
        EpfdFixType PositionFixType { get; }
    }
}