using System.Text;
using Ais.Net.Receiver.Receiver;
using Shouldly;
using Spectre.IO;
using Spectre.IO.Testing;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class FileStreamNmeaReceiverTests
{
    private FakeFileSystem fileSystem = null!;
    private readonly FilePath testFilePath = new("/test/nmea.txt");

    [TestInitialize]
    public void Setup() => this.fileSystem = new FakeFileSystem(FakeEnvironment.CreateLinuxEnvironment());

    [TestMethod]
    public async Task GetAsync_ReadsLinesFromFile()
    {
        // Arrange
        string[] lines =
        [
            "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24",
            "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"
        ];

        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Join(System.Environment.NewLine, lines));

        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath);

        // Act
        List<ReadOnlyMemory<byte>> result = await receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        Encoding.ASCII.GetString(result[0].Span).ShouldBe(lines[0]);
        Encoding.ASCII.GetString(result[1].Span).ShouldBe(lines[1]);
    }

    [TestMethod]
    public async Task GetAsync_WithDelay_ReadsLinesWithDelay()
    {
        // Arrange
        string[] lines = ["Line1", "Line2"];
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Join(System.Environment.NewLine, lines));

        TimeSpan delay = TimeSpan.FromMilliseconds(50);
        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath, delay);

        // Act
        DateTime start = DateTime.UtcNow;
        List<ReadOnlyMemory<byte>> result = await receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None);
        TimeSpan elapsed = DateTime.UtcNow - start;

        // Assert
        result.Count.ShouldBe(2);
        // Should take at least 2 * delay (actually delay is before each read, so 2 delays)
        elapsed.ShouldBeGreaterThanOrEqualTo(delay * 2);
    }

    [TestMethod]
    public async Task GetAsync_EmptyFile_ReturnsNoLines()
    {
        // Arrange
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Empty);
        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath);

        // Act
        List<ReadOnlyMemory<byte>> result = await receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(0);
    }

    [TestMethod]
    public async Task GetAsync_WithCancellation_StopsReading()
    {
        // Arrange
        string[] lines = ["Line1", "Line2", "Line3", "Line4", "Line5"];
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Join(System.Environment.NewLine, lines));

        TimeSpan delay = TimeSpan.FromMilliseconds(50);
        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath, delay);

        using CancellationTokenSource cts = new();
        List<ReadOnlyMemory<byte>> result = [];

        // Act
        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cts.Token))
        {
            result.Add(line);
            if (result.Count >= 2)
            {
                await cts.CancelAsync();  // Synchronous cancel is sufficient since we break immediately
                break;
            }
        }

        // Assert - should have stopped after 2 lines
        result.Count.ShouldBe(2);
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("Line1");
        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath);

        // Act & Assert - should not throw
        await receiver.DisposeAsync();
        await receiver.DisposeAsync();
    }
}