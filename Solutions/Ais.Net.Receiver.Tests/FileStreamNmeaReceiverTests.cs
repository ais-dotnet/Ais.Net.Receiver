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
    public async Task GetAsync_WhenDelayConfigured_WaitsBeforeEachLine()
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

    [TestMethod]
    public async Task GetAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange - don't create the file, it should not exist
        FilePath nonExistentPath = new("/test/nonexistent.txt");
        FileStreamNmeaReceiver receiver = new(this.fileSystem, nonExistentPath);

        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(
            receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task GetAsync_WithOnlyWhitespaceLines_ReturnsWhitespaceLines()
    {
        // Arrange - file with whitespace-only lines
        string[] lines = ["  ", "\t", "   \t   "];
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Join(System.Environment.NewLine, lines));

        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath);

        // Act
        List<ReadOnlyMemory<byte>> result = await receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None);

        // Assert - whitespace lines should be returned as-is
        result.Count.ShouldBe(3);
    }

    [TestMethod]
    public async Task GetAsync_SingleLineNoNewline_ReturnsLine()
    {
        // Arrange - file with single line and no trailing newline
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent("SingleLine");

        FileStreamNmeaReceiver receiver = new(this.fileSystem, this.testFilePath);

        // Act
        List<ReadOnlyMemory<byte>> result = await receiver.GetAsync(CancellationToken.None).ToListAsync(CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
        System.Text.Encoding.ASCII.GetString(result[0].Span).ShouldBe("SingleLine");
    }
}