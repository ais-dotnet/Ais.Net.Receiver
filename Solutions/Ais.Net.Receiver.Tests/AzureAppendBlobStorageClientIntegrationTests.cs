using System.Text;

using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Microsoft.Extensions.Time.Testing;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Integration tests for <see cref="AzureAppendBlobStorageClient"/> against a real Azurite blob
/// endpoint. Container lifetime, Docker detection and per-test isolation come from
/// <see cref="AzuriteIntegrationTestBase"/>.
/// </summary>
[TestClass]
public class AzureAppendBlobStorageClientIntegrationTests : AzuriteIntegrationTestBase
{
    [TestMethod]
    public async Task PersistAsync_WritesNewlineSeparatedBytes_WithoutBom()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 7, 22, 17, 30, 0, TimeSpan.Zero));
        StorageConfig config = this.CreateStorageConfig();

        using (AzureAppendBlobStorageClient client = new(config, timeProvider))
        {
            await client.PersistAsync(
            [
                Line("!AIVDM,1,1,,A,LINE-ONE,0*00"),
                Line("!AIVDM,1,1,,A,LINE-TWO,0*00"),
            ]);
        }

        string path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());
        byte[] written = await this.DownloadBytesAsync(path);

        written.ShouldBe(Encoding.ASCII.GetBytes("!AIVDM,1,1,,A,LINE-ONE,0*00\n!AIVDM,1,1,,A,LINE-TWO,0*00\n"));

        bool hasBom = written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF;
        hasBom.ShouldBeFalse();
    }

    [TestMethod]
    public async Task PersistAsync_RollsOverToNewBlobAtHourBoundary()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 7, 22, 17, 59, 0, TimeSpan.Zero));
        StorageConfig config = this.CreateStorageConfig();
        string hour17Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());

        using AzureAppendBlobStorageClient client = new(config, timeProvider);
        await client.PersistAsync([Line("HOUR-17")]);

        timeProvider.Advance(TimeSpan.FromMinutes(2)); // -> 18:01
        string hour18Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());
        await client.PersistAsync([Line("HOUR-18")]);

        hour17Path.ShouldNotBe(hour18Path);
        (await this.DownloadTextAsync(hour17Path)).ShouldBe("HOUR-17\n");
        (await this.DownloadTextAsync(hour18Path)).ShouldBe("HOUR-18\n");
    }

    private static ReadOnlyMemory<byte> Line(string text) => Encoding.ASCII.GetBytes(text);
}
