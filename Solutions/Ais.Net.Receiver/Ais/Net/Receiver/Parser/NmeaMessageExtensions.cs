// <copyright file="NmeaMessageExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Linq;

namespace Ais.Net.Receiver.Parser;

public static class NmeaMessageExtensions
{
    extension(string message)
    {
        public bool IsMissingNmeaBlockTags() => message.AsSpan()[0] == '!';

        public string PrependNmeaBlockTags()
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Some messages are missing NMEA Block Tags - see https://gpsd.gitlab.io/gpsd/AIVDM.html#_nmea_tag_blocks
            // s: <string> = source stations - in our case AIS.Net.Receiver = 1000001
            // c: <int> = UNIX time in seconds or milliseconds + checksum
            return $@"\s:1000001,c:{timestamp}*{NmeaChecksum("c:" + timestamp)}\{message}";
        }
    }

    public static bool IsMissingNmeaBlockTags(this ReadOnlySpan<byte> message) => message.Length > 0 && message[0] == '!';

    public static ReadOnlyMemory<byte> PrependNmeaBlockTags(this ReadOnlyMemory<byte> message)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string prefix = $@"\s:1000001,c:{timestamp}*{NmeaChecksum("c:" + timestamp)}\";
        byte[] prefixBytes = System.Text.Encoding.ASCII.GetBytes(prefix);
        
        byte[] result = new byte[prefixBytes.Length + message.Length];
        prefixBytes.CopyTo(result, 0);
        message.CopyTo(result.AsMemory(prefixBytes.Length));
        
        return result;
    }

    private static string NmeaChecksum(string s) => s.Aggregate(0, (t, c) => t ^ c).ToString("X2");
}