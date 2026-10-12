// <copyright file="NmeaLineReaderTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.IO.Pipelines;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NmeaLineReaderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReadLineAsync_LfTerminatedLines_ReturnsEachLine()
    {
        List<string> lines = await this.ReadAllAsync("Line1\nLine2\n");

        lines.ShouldBe(["Line1", "Line2"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_CrLfTerminatedLines_TrimsCarriageReturn()
    {
        List<string> lines = await this.ReadAllAsync("Line1\r\nLine2\r\n");

        lines.ShouldBe(["Line1", "Line2"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_EmptyLineBetweenNewlines_ReturnsEmptyLine()
    {
        List<string> lines = await this.ReadAllAsync("Line1\n\nLine2\n");

        lines.ShouldBe(["Line1", string.Empty, "Line2"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_FinalUnterminatedLine_ReturnsLineAtEndOfStream()
    {
        List<string> lines = await this.ReadAllAsync("Line1\nLine2");

        lines.ShouldBe(["Line1", "Line2"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_EmptyStream_ReturnsNull()
    {
        await using NmeaLineReader reader = NmeaLineReader.Create(new MemoryStream());

        ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationToken);

        line.ShouldBeNull();
    }

    [TestMethod]
    public async Task ReadLineAsync_BytesAbove0x7F_ArePreserved()
    {
        byte[] bytes = [(byte)'!', 0xE9, 0xFF, (byte)'A', (byte)'\n'];
        await using NmeaLineReader reader = NmeaLineReader.Create(new MemoryStream(bytes));

        ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationToken);

        line.ShouldNotBeNull();
        line.Value.ToArray().ShouldBe([(byte)'!', 0xE9, 0xFF, (byte)'A']);
    }

    [TestMethod]
    public async Task ReadLineAsync_OverLongTerminatedLine_IsDiscardedAndNextLineArrives()
    {
        string tooLong = new('x', 20);
        List<string> lines = await this.ReadAllAsync($"{tooLong}\nOk\n", maxLineLength: 16);

        lines.ShouldBe(["Ok"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_OverLongUnterminatedData_IsDiscardedAndResyncsAtNextNewline()
    {
        // Feed more than the cap with no newline, so the reader must drop the partial line while
        // the stream is still open, then supply the tail of that line and a good one.
        Pipe pipe = new();
        await using NmeaLineReader reader = new(pipe.Reader, maxLineLength: 16);

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(new string('x', 40)), this.TestContext.CancellationToken);
        Task<ReadOnlyMemory<byte>?> pending = reader.ReadLineAsync(this.TestContext.CancellationToken).AsTask();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("tail\nOk\n"), this.TestContext.CancellationToken);
        await pipe.Writer.CompleteAsync();

        ReadOnlyMemory<byte>? first = await pending;
        ReadOnlyMemory<byte>? second = await reader.ReadLineAsync(this.TestContext.CancellationToken);

        // Neither the over-long prefix nor its remainder ("tail") surfaces: framing resumes after
        // the newline that ends the dropped line.
        Encoding.ASCII.GetString(first!.Value.Span).ShouldBe("Ok");
        second.ShouldBeNull();
    }

    [TestMethod]
    public async Task ReadLineAsync_OverLongUnterminatedDataAtEndOfStream_IsDropped()
    {
        string tooLong = new('x', 20);
        List<string> lines = await this.ReadAllAsync($"Ok\n{tooLong}", maxLineLength: 16);

        lines.ShouldBe(["Ok"]);
    }

    [TestMethod]
    public async Task ReadLineAsync_LineSplitAcrossTwoReads_IsReassembled()
    {
        Pipe pipe = new();
        await using NmeaLineReader reader = new(pipe.Reader);

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes("!AIVDM,1,1,"), this.TestContext.CancellationToken);
        Task<ReadOnlyMemory<byte>?> pending = reader.ReadLineAsync(this.TestContext.CancellationToken).AsTask();

        await Task.Yield();
        pending.IsCompleted.ShouldBeFalse();

        await pipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(",A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24\r\n"), this.TestContext.CancellationToken);

        ReadOnlyMemory<byte>? line = await pending;

        Encoding.ASCII.GetString(line!.Value.Span).ShouldBe("!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24");
    }

    [TestMethod]
    public async Task ReadLineAsync_ReusesBuffer_FirstLineIsOverwrittenBySecond()
    {
        await using NmeaLineReader reader = NmeaLineReader.Create(new MemoryStream(Encoding.ASCII.GetBytes("AAAA\nBBBB\n")));

        ReadOnlyMemory<byte> first = (await reader.ReadLineAsync(this.TestContext.CancellationToken))!.Value;
        Encoding.ASCII.GetString(first.Span).ShouldBe("AAAA");

        ReadOnlyMemory<byte> second = (await reader.ReadLineAsync(this.TestContext.CancellationToken))!.Value;

        Encoding.ASCII.GetString(second.Span).ShouldBe("BBBB");
        Encoding.ASCII.GetString(first.Span).ShouldBe("BBBB");
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        NmeaLineReader reader = NmeaLineReader.Create(new MemoryStream());

        await reader.DisposeAsync();
        await reader.DisposeAsync();
    }

    [TestMethod]
    public async Task DisposeAsync_LeavesStreamOpen()
    {
        MemoryStream stream = new(Encoding.ASCII.GetBytes("Line1\n"));
        NmeaLineReader reader = NmeaLineReader.Create(stream);

        await reader.DisposeAsync();

        stream.CanRead.ShouldBeTrue();
    }

    private async Task<List<string>> ReadAllAsync(string content, int maxLineLength = 8192)
    {
        await using NmeaLineReader reader = NmeaLineReader.Create(new MemoryStream(Encoding.ASCII.GetBytes(content)), maxLineLength: maxLineLength);
        List<string> lines = [];

        while (await reader.ReadLineAsync(this.TestContext.CancellationToken) is { } line)
        {
            lines.Add(Encoding.ASCII.GetString(line.Span));
        }

        return lines;
    }
}
