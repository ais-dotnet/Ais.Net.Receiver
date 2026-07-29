// <copyright file="NmeaMessageExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers.Text;

namespace Ais.Net.Receiver.Parser;

public static class NmeaMessageExtensions
{
    /// <summary>
    /// The largest tag-block prefix <see cref="PrependNmeaBlockTags(ReadOnlySpan{byte}, TimeProvider, Span{byte})"/>
    /// can produce: <c>\s:1000001,c:</c> (13 bytes) + up to 20 timestamp digits + <c>*CK\</c> (4 bytes).
    /// A caller-supplied destination must have room for <c>message.Length + MaxPrefixLength</c> bytes.
    /// </summary>
    public const int MaxPrefixLength = 13 + 20 + 4;

    extension(string message)
    {
        public bool IsMissingNmeaBlockTags() => message.Length > 0 && message[0] == '!';

        public string PrependNmeaBlockTags(TimeProvider timeProvider)
        {
            string timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString();

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
            if (message.Length == 0 || message[0] != (byte)'\\')
            {
                return (StationId: 0, UnixTimestamp: 0);
            }

            int endOfTags = message[1..].IndexOf((byte)'\\');
            if (endOfTags == -1)
            {
                return (StationId: 0, UnixTimestamp: 0);
            }

            ReadOnlySpan<byte> tags = message.Slice(1, endOfTags);

            int stationId = 0;
            long timestamp = 0;

            // Parse the comma-separated tag fields directly over the bytes. This runs once per
            // received message, so we avoid allocating an intermediate string, a string[] from
            // Split(',') and per-field substrings on this hot path.
            while (!tags.IsEmpty)
            {
                int comma = tags.IndexOf((byte)',');
                ReadOnlySpan<byte> part = comma == -1 ? tags : tags[..comma];
                tags = comma == -1 ? default : tags[(comma + 1)..];

                if (part.StartsWith("s:"u8))
                {
                    ReadOnlySpan<byte> value = part[2..];
                    int digits = 0;
                    while (digits < value.Length && value[digits] is >= (byte)'0' and <= (byte)'9')
                    {
                        digits++;
                    }

                    if (digits > 0 && Utf8Parser.TryParse(value[..digits], out int parsedStationId, out _))
                    {
                        stationId = parsedStationId;
                    }
                }
                else if (part.StartsWith("c:"u8))
                {
                    ReadOnlySpan<byte> value = part[2..];
                    int checksumIndex = value.IndexOf((byte)'*');
                    if (checksumIndex != -1)
                    {
                        value = value[..checksumIndex];
                    }

                    Utf8Parser.TryParse(value, out timestamp, out _);
                }
            }

            return (StationId: stationId, UnixTimestamp: timestamp);
        }

        /// <summary>
        /// Writes <c>\s:1000001,c:&lt;timestamp&gt;*&lt;checksum&gt;\&lt;message&gt;</c> into
        /// <paramref name="destination"/> and returns the number of bytes written. Allocation-free:
        /// the caller owns the buffer (see <see cref="MaxPrefixLength"/> for sizing), enabling a
        /// reusable buffer on the receive path instead of a fresh array per message.
        /// </summary>
        public int PrependNmeaBlockTags(TimeProvider timeProvider, Span<byte> destination) =>
            WritePrependedNmeaBlockTags(message, timeProvider.GetUtcNow().ToUnixTimeSeconds(), destination);
    }

    extension(ReadOnlyMemory<byte> message)
    {
        public ReadOnlyMemory<byte> PrependNmeaBlockTags(TimeProvider timeProvider)
        {
            long unixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            byte[] result = new byte[message.Length + MaxPrefixLength];
            int written = WritePrependedNmeaBlockTags(message.Span, unixSeconds, result);
            return result.AsMemory(0, written);
        }
    }

    private static int WritePrependedNmeaBlockTags(ReadOnlySpan<byte> message, long unixSeconds, Span<byte> destination)
    {
        // Format the timestamp straight to bytes so it can be both checksummed and written
        // into the destination without allocating intermediate strings.
        Span<byte> timestamp = stackalloc byte[20];
        Utf8Formatter.TryFormat(unixSeconds, timestamp, out int timestampLength);
        timestamp = timestamp[..timestampLength];

        // NMEA tag-block checksum: XOR of the ASCII bytes of "c:<timestamp>".
        int checksum = 'c' ^ ':';
        foreach (byte b in timestamp)
        {
            checksum ^= b;
        }

        ReadOnlySpan<byte> head = @"\s:1000001,c:"u8;
        head.CopyTo(destination);
        int position = head.Length;
        timestamp.CopyTo(destination[position..]);
        position += timestamp.Length;
        destination[position++] = (byte)'*';
        destination[position++] = ToHexUpper((checksum >> 4) & 0xF);
        destination[position++] = ToHexUpper(checksum & 0xF);
        destination[position++] = (byte)'\\';
        message.CopyTo(destination[position..]);
        return position + message.Length;

        static byte ToHexUpper(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
    }

    private static string NmeaChecksum(string s)
    {
        int checksum = 0;
        foreach (char c in s)
        {
            checksum ^= c;
        }

        return checksum.ToString("X2");
    }
}
