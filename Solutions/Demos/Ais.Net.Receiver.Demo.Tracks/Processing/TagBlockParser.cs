// <copyright file="TagBlockParser.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public static class TagBlockParser
{
    public static long ExtractEpoch(string sentence)
    {
        // Format: \s:2573210,c:1614556795*03\!BSVDM,...
        int cIndex = sentence.IndexOf("c:", StringComparison.Ordinal);
        if (cIndex < 0) return 0;

        int start = cIndex + 2;
        int end = start;
        while (end < sentence.Length && char.IsDigit(sentence[end]))
        {
            end++;
        }

        if (end > start && long.TryParse(sentence.AsSpan(start, end - start), out long epoch))
        {
            return epoch;
        }

        return 0;
    }
}
