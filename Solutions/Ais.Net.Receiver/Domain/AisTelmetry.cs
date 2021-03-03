namespace Ais.Net.Receiver.Domain
{
    public class AisTelmetry : AisMessageBase, IVesselPosition, IVesselName
    {
        public string VesselName { get; set; }

        public Position Position { get; set; }
    }
}