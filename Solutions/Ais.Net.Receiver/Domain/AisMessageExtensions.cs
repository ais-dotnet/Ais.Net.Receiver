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

        public static string TextFieldToString(this NmeaAisTextFieldParser field)
        {
            Span<byte> ascii = stackalloc byte[(int)field.CharacterCount];
            field.WriteAsAscii(ascii);
            return Encoding.ASCII.GetString(ascii);
        }
    }
}