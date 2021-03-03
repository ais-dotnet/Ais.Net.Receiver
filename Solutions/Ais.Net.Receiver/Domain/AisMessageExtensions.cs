// <copyright file="NmeaReceiver.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

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

        public static string CleanVesselName(this string value)
        {
            return value.Trim('@').Trim().Replace("  ", " ");
        }

        public static string GetString(this Span<byte> value)
        {
            return Encoding.ASCII.GetString(value); ;
        }

        public static string TextFieldToString(this NmeaAisTextFieldParser field)
        {
            Span<byte> ascii = stackalloc byte[(int)field.CharacterCount];
            field.WriteAsAscii(ascii);
            return Encoding.ASCII.GetString(ascii);
        }
    }
}