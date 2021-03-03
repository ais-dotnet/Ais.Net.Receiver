namespace Ais.Net.Receiver.Domain
{
    using System;
    using System.Text;

    public static class AisMessageExtensions
    {
        public static double From10000thMinsToDegrees(this int value)
        {
            return value / 600000.0;
        }

        public static string ParseVesselName(this Span<byte> value)
        {
            var vesselName = Encoding.ASCII.GetString(value);

            return vesselName.Trim('@').Trim().Replace("  ", " ");
        }
    }
}