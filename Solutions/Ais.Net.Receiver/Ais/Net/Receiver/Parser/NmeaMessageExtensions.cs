// <copyright file="NmeaMessageExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

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

    extension(ReadOnlySpan<byte> message)
    {
        public bool IsMissingNmeaBlockTags => message.Length > 0 && message[0] == '!';

        public (int StationId, long UnixTimestamp) ParseNmeaBlockTags()
        {
            if (message.Length == 0 || message[0] != '\\')
            {
                return (StationId: 0, UnixTimestamp: 0);
            }

            int endOfTags = message[1..].IndexOf((byte)'\\');
            if (endOfTags == -1)
            {
                return (StationId: 0, UnixTimestamp: 0);
            }

            ReadOnlySpan<byte> tags = message.Slice(1, endOfTags);
            string tagsString = System.Text.Encoding.ASCII.GetString(tags);

            int stationId = 0;
            long timestamp = 0;

            foreach (string part in tagsString.Split(','))
            {
                if (part.StartsWith("s:"))
                {
                    string val = part[2..];
                    int len = 0;
                    while (len < val.Length && char.IsDigit(val[len]))
                    {
                        len++;
                    }

                    if (len > 0 && int.TryParse(val.AsSpan(0, len), out int sid))
                    {
                        stationId = sid;
                    }
                }
                else if (part.StartsWith("c:"))
                {
                    string val = part[2..];
                    int checksumIndex = val.IndexOf('*');
                    if (checksumIndex != -1)
                    {
                        val = val[..checksumIndex];
                    }

                    long.TryParse(val, out timestamp);
                }
            }

            return (StationId: stationId, UnixTimestamp: timestamp);
        }
    }

    extension(ReadOnlyMemory<byte> message)
    {
        public ReadOnlyMemory<byte> PrependNmeaBlockTags()
        {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string prefix = $@"\s:1000001,c:{timestamp}*{NmeaChecksum("c:" + timestamp)}\";
            byte[] prefixBytes = System.Text.Encoding.ASCII.GetBytes(prefix);
            
            byte[] result = new byte[prefixBytes.Length + message.Length];
            prefixBytes.CopyTo(result, 0);
            message.CopyTo(result.AsMemory(prefixBytes.Length));
            
            return result;
        }
    }

    private static string NmeaChecksum(string s) => s.Aggregate(0, (t, c) => t ^ c).ToString("X2");
}