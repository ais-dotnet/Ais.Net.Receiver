// <copyright file="DeckGlJsonWriter.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using Ais.Net.Receiver.Demo.Tracks.Models;

namespace Ais.Net.Receiver.Demo.Tracks.Output;

/// <summary>
/// Writes vessel tracks in the shape the deck.gl frontend loads: a metadata header plus one entry per
/// vessel, with timestamps rebased to seconds from the start of the recording so the browser can index
/// them as small integers.
/// </summary>
public static class DeckGlJsonWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes tracks to a stream. The visualiser uses this to write a replay straight to the HTTP
    /// response rather than staging a file first.
    /// </summary>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="sourceFile">A label for the source, echoed in the output metadata.</param>
    /// <param name="tracks">The tracks to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the JSON has been written.</returns>
    public static Task SerializeAsync(
        Stream destination,
        string sourceFile,
        IReadOnlyCollection<VesselTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(tracks);

        return JsonSerializer.SerializeAsync(destination, Build(sourceFile, tracks), SerializerOptions, cancellationToken);
    }

    private static OutputRoot Build(string sourceFile, IReadOnlyCollection<VesselTrack> tracks)
    {
        long minEpoch = long.MaxValue;
        long maxEpoch = long.MinValue;
        int totalPoints = 0;

        foreach (var track in tracks)
        {
            foreach (var p in track.Positions)
            {
                if (p.Epoch < minEpoch) minEpoch = p.Epoch;
                if (p.Epoch > maxEpoch) maxEpoch = p.Epoch;
            }
            totalPoints += track.Positions.Count;
        }

        long baseEpoch = minEpoch;

        return new OutputRoot
        {
            Metadata = new OutputMetadata
            {
                SourceFile = sourceFile,
                TimeRange = new TimeRange
                {
                    Start = 0,
                    End = (int)(maxEpoch - baseEpoch)
                },
                BaseEpoch = baseEpoch,
                VesselCount = tracks.Count,
                TotalPoints = totalPoints
            },
            Vessels = tracks
                .Where(t => t.Positions.Count >= 2)
                .OrderByDescending(t => t.Positions.Count)
                .Select(t => new OutputVessel
                {
                    Mmsi = t.Mmsi,
                    Name = string.IsNullOrEmpty(t.Metadata.Name) ? $"MMSI {t.Mmsi}" : t.Metadata.Name,
                    ShipType = t.Metadata.ShipType,
                    ShipTypeCategory = t.Metadata.ShipTypeCategory,
                    Color = t.Metadata.Color,
                    Positions = t.Positions
                        .OrderBy(p => p.Epoch)
                        .Select(p => new OutputPosition
                        {
                            Coordinates = [
                                Math.Round(p.Longitude, 6),
                                Math.Round(p.Latitude, 6)
                            ],
                            Timestamp = (int)(p.Epoch - baseEpoch),
                            Speed = MathF.Round(p.SpeedOverGround, 1),
                            Course = MathF.Round(p.CourseOverGround, 1)
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private class OutputRoot
    {
        public OutputMetadata Metadata { get; set; } = new();
        public List<OutputVessel> Vessels { get; set; } = [];
    }

    private class OutputMetadata
    {
        public string SourceFile { get; set; } = string.Empty;
        public TimeRange TimeRange { get; set; } = new();
        public long BaseEpoch { get; set; }
        public int VesselCount { get; set; }
        public int TotalPoints { get; set; }
    }

    private class TimeRange
    {
        public int Start { get; set; }
        public int End { get; set; }
    }

    private class OutputVessel
    {
        public uint Mmsi { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShipType { get; set; } = string.Empty;
        public string ShipTypeCategory { get; set; } = string.Empty;
        public int[] Color { get; set; } = [];
        public List<OutputPosition> Positions { get; set; } = [];
    }

    private class OutputPosition
    {
        public double[] Coordinates { get; set; } = [];
        public int Timestamp { get; set; }
        public float Speed { get; set; }
        public float Course { get; set; }
    }
}
