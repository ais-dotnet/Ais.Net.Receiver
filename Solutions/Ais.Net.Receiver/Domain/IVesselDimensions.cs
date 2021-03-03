namespace Ais.Net.Receiver.Domain
{
    public interface IVesselDimensions
    {
        uint DimensionToBow { get; }
        uint DimensionToPort { get; }
        uint DimensionToStarboard { get; }
        uint DimensionToStern { get; }
    }
}