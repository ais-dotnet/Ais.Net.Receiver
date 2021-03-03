namespace Ais.Net.Receiver.Domain
{
    public interface IVesselDimensions
    {
        uint DimensionToBow { get; set; }
        uint DimensionToPort { get; set; }
        uint DimensionToStarboard { get; set; }
        uint DimensionToStern { get; set; }
    }
}