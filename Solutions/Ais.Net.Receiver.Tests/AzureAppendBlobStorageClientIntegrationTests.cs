using System.Text;

using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Azure.Storage.Blobs;

using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Testcontainers.Azurite;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Integration tests for <see cref="AzureAppendBlobStorageClient"/> against a real Azurite blob
/// endpoint (via Testcontainers). Skipped as inconclusive when Docker is not available.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class AzureAppendBlobStorageClientIntegrationTests
{
    private static AzuriteContainer? container;
    private static string? connectionString;
    private static string? skipReason;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Azurite lags the newest service API version the Azure SDK sends, so skip that check
        // (WithCommand appends to Azurite's default startup args).
        AzuriteContainer azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithCommand("--skipApiVersionCheck")
            .Build();
        try
        {
            await azurite.StartAsync();
            container = azurite;
            connectionString = azurite.GetConnectionString();
        }
        catch (Exception ex)
        {
            skipReason = "Azurite container could not start (is Docker running?): " + ex.Message;
            await azurite.DisposeAsync();
        }
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [TestInitialize]
    public void SkipWhenAzuriteUnavailable()
    {
        if (connectionString is null)
        {
            Assert.Inconclusive(skipReason ?? "Azurite is unavailable.");
        }
    }

    [TestMethod]
    public async Task PersistAsync_WritesNewlineSeparatedBytes_WithoutBom()
    {
        string containerName = NewContainerName();
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 7, 22, 17, 30, 0, TimeSpan.Zero));
        StorageConfig config = Config(containerName);

        using (AzureAppendBlobStorageClient client = new(config, timeProvider))
        {
            await client.PersistAsync(
            [
                Line("!AIVDM,1,1,,A,LINE-ONE,0*00"),
                Line("!AIVDM,1,1,,A,LINE-TWO,0*00"),
            ]);
        }

        string path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());
        byte[] written = await DownloadBytesAsync(containerName, path);

        written.ShouldBe(Encoding.ASCII.GetBytes("!AIVDM,1,1,,A,LINE-ONE,0*00\n!AIVDM,1,1,,A,LINE-TWO,0*00\n"));

        bool hasBom = written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF;
        hasBom.ShouldBeFalse();
    }

    [TestMethod]
    public async Task PersistAsync_RollsOverToNewBlobAtHourBoundary()
    {
        string containerName = NewContainerName();
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 7, 22, 17, 59, 0, TimeSpan.Zero));
        StorageConfig config = Config(containerName);
        string hour17Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());

        using AzureAppendBlobStorageClient client = new(config, timeProvider);
        await client.PersistAsync([Line("HOUR-17")]);

        timeProvider.Advance(TimeSpan.FromMinutes(2)); // -> 18:01
        string hour18Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(timeProvider.GetUtcNow());
        await client.PersistAsync([Line("HOUR-18")]);

        hour17Path.ShouldNotBe(hour18Path);
        Encoding.ASCII.GetString(await DownloadBytesAsync(containerName, hour17Path)).ShouldBe("HOUR-17\n");
        Encoding.ASCII.GetString(await DownloadBytesAsync(containerName, hour18Path)).ShouldBe("HOUR-18\n");
    }

    private static ReadOnlyMemory<byte> Line(string text) => Encoding.ASCII.GetBytes(text);

    private static string NewContainerName() => "test-" + Guid.NewGuid().ToString("N");

    private static StorageConfig Config(string containerName) => new()
    {
        ConnectionString = connectionString!,
        ContainerName = containerName,
        EnableCapture = true,
    };

    private static async Task<byte[]> DownloadBytesAsync(string containerName, string path)
    {
        BlobContainerClient containerClient = new(connectionString, containerName);
        return (await containerClient.GetBlobClient(path).DownloadContentAsync()).Value.Content.ToArray();
    }
}
