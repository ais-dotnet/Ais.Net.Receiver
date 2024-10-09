﻿// <copyright file="NmeaMessageExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Linq;

namespace Ais.Net.Receiver.Parser;

public static class NmeaMessageExtensions
{
    public static bool IsMissingNmeaBlockTags(this string message)
    {
        return message.AsSpan()[0] == '!';
    }

    public static string PrependNmeaBlockTags(this string message)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // Some messages are missing NMEA Block Tags - see https://gpsd.gitlab.io/gpsd/AIVDM.html#_nmea_tag_blocks
        // s: <string> = source stations - in our case AIS.Net.Receiver = 1000001
        // c: <int> = UNIX time in seconds or milliseconds + checksum
        return $@"\s:1000001,c:{timestamp}*{NmeaChecksum("c:" + timestamp)}\{message}";
    }

    private static string NmeaChecksum(string s) => s.Aggregate(0, (t, c) => t ^ c).ToString("X2");
}