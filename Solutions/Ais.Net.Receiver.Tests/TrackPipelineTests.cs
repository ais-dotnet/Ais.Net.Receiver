// <copyright file="TrackPipelineTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks;
using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Receiver;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Covers the demo's track pipeline end to end through the receiver's decode path - in particular
/// that each position's epoch comes from the tag block of the sentence that produced it, via the
/// receiver's <see cref="ReceiverHost.Metadata"/> stream.
/// </summary>
[TestClass]
public class TrackPipelineTests
{
    [TestMethod]
    public async Task BuildAsync_TimestampsPositionsFromTheSentenceTagBlocks()
    {
        // Real sentences from the Norwegian feed: two position reports for one vessel, sixty seconds
        // apart, and a single report from a second vessel - which the >= 2 positions rule discards.
        INmeaReceiver receiver = Substitute.For<INmeaReceiver>();
        receiver.GetAsync(Arg.Any<CancellationToken>()).Returns(new ReadOnlyMemory<byte>[]
        {
            "\\s:2573210,c:1614556795*03\\!BSVDM,1,1,,A,13c6@t0PBR0G5d6QQVgFKm9f0`PB,0*70"u8.ToArray(),
            "\\s:2573210,c:1614556855*00\\!BSVDM,1,1,,A,13c6@t002T0G6E`QQLF6Om7f0T00,0*75"u8.ToArray(),
            "\\s:2573595,c:1614556794*08\\!BSVDM,1,1,,A,13m;PJ?00025:B8`RpHL=D5f26sd,0*46"u8.ToArray(),
        }.ToAsyncEnumerable());

        IReadOnlyList<VesselTrack> tracks = await TrackPipeline.BuildAsync(receiver);

        VesselTrack track = tracks.ShouldHaveSingleItem();
        track.Mmsi.ShouldNotBe(0u);
        track.Positions.Select(p => p.Epoch).ShouldBe([1614556795, 1614556855]);

        // The recording is from the Skagerrak, so a decode that drifted would fall outside this box.
        track.Positions.ShouldAllBe(p => p.Latitude > 50 && p.Latitude < 75 && p.Longitude > 0 && p.Longitude < 30);
    }

    [TestMethod]
    public async Task BuildAsync_FaultBeforeAnyMessage_Propagates()
    {
        // A missing blob or a bad connection string faults before a single message decodes. That must
        // surface: swallowing it would let ReplayTrackSource cache an empty replay for a typo.
        INmeaReceiver receiver = Substitute.For<INmeaReceiver>();
        receiver.GetAsync(Arg.Any<CancellationToken>()).Throws(new IOException("storage unavailable"));

        await Should.ThrowAsync<IOException>(() => TrackPipeline.BuildAsync(receiver));
    }

    [TestMethod]
    public async Task BuildAsync_FaultAfterMessages_IsTheEndOfTheRecording()
    {
        // A finite network-style source ends by faulting once the far end goes away; after data has
        // flowed that is completion, not failure.
        INmeaReceiver receiver = Substitute.For<INmeaReceiver>();
        receiver.GetAsync(Arg.Any<CancellationToken>()).Returns(OneThenFault());

        IReadOnlyList<VesselTrack> tracks = await TrackPipeline.BuildAsync(receiver);

        // The single position is filtered by the >= 2 rule; the point is that no exception escaped.
        tracks.ShouldBeEmpty();

        static async IAsyncEnumerable<ReadOnlyMemory<byte>> OneThenFault()
        {
            await Task.Yield();
            yield return "\\s:2573210,c:1614556795*03\\!BSVDM,1,1,,A,13c6@t0PBR0G5d6QQVgFKm9f0`PB,0*70"u8.ToArray();
            throw new IOException("connection reset");
        }
    }
}
