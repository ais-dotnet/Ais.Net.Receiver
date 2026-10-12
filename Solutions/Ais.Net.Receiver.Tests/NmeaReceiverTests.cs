// <copyright file="NmeaReceiverTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

using Spectre.IO;
using Spectre.IO.Testing;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NmeaReceiverTests
{
    private readonly FilePath testFilePath = new("/test/nmea.txt");
    private FakeFileSystem fileSystem = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup() => this.fileSystem = new FakeFileSystem(FakeEnvironment.CreateLinuxEnvironment());

    [TestMethod]
    public void Constructor_FileConnection_IsFinite()
    {
        FileNmeaConnection connection = new(this.fileSystem, this.testFilePath);

        connection.Kind.ShouldBe(NmeaSourceKind.Finite);
        connection.Transport.ShouldBe("file");
        connection.EndpointPort.ShouldBe(0);
        connection.EndpointAddress.ShouldBe(this.testFilePath.FullPath);
    }

    [TestMethod]
    public async Task GetAsync_FiniteSource_CompletesAtEndOfStreamWithAllLines()
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("Line1\nLine2\r\nLine3");
        await using NmeaReceiver receiver = this.CreateReceiver();

        List<string> lines = await ReadAllAsync(receiver, this.TestContext.CancellationToken);

        lines.ShouldBe(["Line1", "Line2", "Line3"]);
    }

    [TestMethod]
    public async Task GetAsync_EmptyFile_CompletesWithNoLines()
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Empty);
        await using NmeaReceiver receiver = this.CreateReceiver();

        List<string> lines = await ReadAllAsync(receiver, this.TestContext.CancellationToken);

        lines.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_MissingFile_ThrowsFileNotFoundExceptionWithoutRetrying()
    {
        List<bool> states = [];
        await using NmeaReceiver receiver = new(
            new FileNmeaConnection(this.fileSystem, new FilePath("/test/missing.txt")),
            TimeProvider.System,
            new NmeaReceiverOptions { RetryPeriodicity = TimeSpan.FromMinutes(10) },
            onConnectionStateChanged: states.Add);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await Should.ThrowAsync<FileNotFoundException>(ReadAllAsync(receiver, this.TestContext.CancellationToken));

        // A retry would wait out the (ten minute) backoff; propagating means no retry happened.
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(1));
        states.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_FiniteSource_ReportsConnectedThenDisconnected()
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("Line1\nLine2\n");
        List<bool> states = [];
        await using NmeaReceiver receiver = new(
            new FileNmeaConnection(this.fileSystem, this.testFilePath),
            TimeProvider.System,
            onConnectionStateChanged: states.Add);

        await ReadAllAsync(receiver, this.TestContext.CancellationToken);

        states.ShouldBe([true, false]);
    }

    [TestMethod]
    public async Task GetAsync_WhenCancelled_StopsReading()
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("Line1\nLine2\nLine3\nLine4\nLine5\n");
        await using NmeaReceiver receiver = this.CreateReceiver(TimeSpan.FromMilliseconds(20));
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        List<string> lines = [];

        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cts.Token))
        {
            lines.Add(Encoding.ASCII.GetString(line.Span));
            if (lines.Count == 2)
            {
                // Cancel without breaking: the receiver must end the sequence quietly on its own.
                await cts.CancelAsync();
            }
        }

        lines.ShouldBe(["Line1", "Line2"]);
    }

    [TestMethod]
    public async Task GetAsync_WithLineDelay_PacesReads()
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("Line1\nLine2\nLine3\n");
        TimeSpan delay = TimeSpan.FromMilliseconds(50);
        await using NmeaReceiver receiver = this.CreateReceiver(delay);

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<string> lines = await ReadAllAsync(receiver, this.TestContext.CancellationToken);

        lines.Count.ShouldBe(3);

        // The delay comes before each read, including the one that finds end of stream.
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(delay * 3);
    }

    private static async Task<List<string>> ReadAllAsync(NmeaReceiver receiver, CancellationToken cancellationToken)
    {
        List<string> lines = [];
        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cancellationToken))
        {
            lines.Add(Encoding.ASCII.GetString(line.Span));
        }

        return lines;
    }

    private NmeaReceiver CreateReceiver(TimeSpan? lineDelay = null) =>
        new(new FileNmeaConnection(this.fileSystem, this.testFilePath, lineDelay), TimeProvider.System);
}
