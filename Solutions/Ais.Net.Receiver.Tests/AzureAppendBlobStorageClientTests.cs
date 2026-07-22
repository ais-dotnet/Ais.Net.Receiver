using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class AzureAppendBlobStorageClientTests
{
    [TestMethod]
    public void StorageConfig_MaxDegreeOfParallelism_DefaultsToOne()
    {
        // Append blobs are written sequentially; concurrent writers reorder blocks and race the
        // hour-boundary blob swap, so the safe default is 1.
        StorageConfig config = new() { ConnectionString = "x", ContainerName = "y" };

        config.MaxDegreeOfParallelism.ShouldBe(1);
    }

    [TestMethod]
    public void GetHourlyBlobPath_ProducesHourlyDatePartitionedPath()
    {
        DateTimeOffset timestamp = new(2026, 7, 22, 17, 45, 30, TimeSpan.Zero);

        AzureAppendBlobStorageClient.GetHourlyBlobPath(timestamp)
            .ShouldBe("raw/2026/07/22/20260722T17.nm4");
    }

    [TestMethod]
    public void GetHourlyBlobPath_SameHour_ProducesSamePath()
    {
        DateTimeOffset early = new(2026, 7, 22, 17, 0, 1, TimeSpan.Zero);
        DateTimeOffset late = new(2026, 7, 22, 17, 59, 59, TimeSpan.Zero);

        AzureAppendBlobStorageClient.GetHourlyBlobPath(early)
            .ShouldBe(AzureAppendBlobStorageClient.GetHourlyBlobPath(late));
    }

    [TestMethod]
    public void GetHourlyBlobPath_RollsOverAtHourBoundary()
    {
        DateTimeOffset before = new(2026, 7, 22, 17, 59, 59, TimeSpan.Zero);
        DateTimeOffset after = new(2026, 7, 22, 18, 0, 0, TimeSpan.Zero);

        AzureAppendBlobStorageClient.GetHourlyBlobPath(before)
            .ShouldNotBe(AzureAppendBlobStorageClient.GetHourlyBlobPath(after));
        AzureAppendBlobStorageClient.GetHourlyBlobPath(after)
            .ShouldBe("raw/2026/07/22/20260722T18.nm4");
    }
}
