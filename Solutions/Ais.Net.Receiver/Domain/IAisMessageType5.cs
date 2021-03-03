namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType5
    {
        uint AisVersion { get; }

        string Destination { get; }

        uint Draught10thMetres { get; }

        uint EtaMonth { get; }

        uint EtaDay { get; }

        uint EtaHour { get; }

        uint EtaMinute { get; }
        uint ImoNumber { get; }

        uint Spare423 { get; }
    }
}