// <copyright file="BlobNmeaReceiverIntegrationTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Sources;

using Azure.Storage.Blobs;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Proves the demo's blob replay source reads a capture back byte-for-byte - including bytes above
/// 0x7F, which the Latin1 handling in <c>StreamNmeaReceiver</c> exists to preserve and which an
/// ASCII reader would corrupt to '?'.
/// </summary>
[TestClass]
public class BlobNmeaReceiverIntegrationTests : AzuriteIntegrationTestBase
{
    [TestMethod]
    public async Task GetAsync_RoundTripsCapturedSentences_IncludingBytesAbove0x7F()
    {
        byte[] line1 = "\\s:2573210,c:1614556795*03\\!BSVDM,1,1,,A,13c6@t0PBR0G5d6QQVgFKm9f0`PB,0*70"u8.ToArray();

        // A tag block carrying a raw 0xB5 byte. The receiver must hand it back unchanged.
        byte[] line2 = [.. "\\s:99,c:1614556796,x:"u8, 0xB5, .. "*00\\!BSVDM,1,1,,A,x,0*00"u8];

        byte[] content = [.. line1, (byte)'\n', .. line2, (byte)'\n'];

        BlobContainerClient container = this.CreateVerificationClient();
        await container.CreateIfNotExistsAsync();
        await container.GetBlobClient("raw/2021/03/01.nm4").UploadAsync(new BinaryData(content));

        await using BlobNmeaReceiver receiver = new(this.ConnectionString, this.ContainerName, "raw/2021/03/01.nm4");

        List<byte[]> lines = [];
        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync())
        {
            lines.Add(line.ToArray());
        }

        lines.Count.ShouldBe(2);
        lines[0].ShouldBe(line1);
        lines[1].ShouldBe(line2);
    }
}
