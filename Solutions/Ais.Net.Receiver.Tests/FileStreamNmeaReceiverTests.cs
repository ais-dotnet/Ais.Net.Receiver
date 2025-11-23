using System.Text;

using Ais.Net.Receiver.Receiver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Spectre.IO.Testing;

namespace Ais.Net.Receiver.Tests
{
    [TestClass]
    public class FileStreamNmeaReceiverTests
    {
        private FakeFileSystem fileSystem = null!;
        private const string TestFilePath = "/test/nmea.txt";

        [TestInitialize]
        public void Setup()
        {
            this.fileSystem = new FakeFileSystem(FakeEnvironment.CreateLinuxEnvironment());
        }

        [TestMethod]
        public async Task GetAsync_ReadsLinesFromFile()
        {
            // Arrange
            string[] lines =
            [
                "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24",
                "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"
            ];
            
            this.fileSystem.CreateFile(TestFilePath).SetTextContent(string.Join(Environment.NewLine, lines));

            FileStreamNmeaReceiver receiver = new(this.fileSystem, TestFilePath);

            // Act
            List<ReadOnlyMemory<byte>> result = await receiver.GetAsync().ToListAsync();

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
            this.fileSystem.CreateFile(TestFilePath).SetTextContent(string.Join(Environment.NewLine, lines));

            TimeSpan delay = TimeSpan.FromMilliseconds(50);
            FileStreamNmeaReceiver receiver = new(this.fileSystem, TestFilePath, delay);

            // Act
            DateTime start = DateTime.UtcNow;
            List<ReadOnlyMemory<byte>> result = await receiver.GetAsync().ToListAsync();
            TimeSpan elapsed = DateTime.UtcNow - start;

            // Assert
            result.Count.ShouldBe(2);
            // Should take at least 2 * delay (actually delay is before each read, so 2 delays)
            elapsed.ShouldBeGreaterThanOrEqualTo(delay * 2);
        }
    }
}