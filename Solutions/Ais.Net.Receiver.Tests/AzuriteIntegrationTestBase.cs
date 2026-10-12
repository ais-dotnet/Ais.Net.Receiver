// <copyright file="AzuriteIntegrationTestBase.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;

using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Base class for tests that run against the shared Azurite instance. Handles acquiring the endpoint
/// (or skipping when Docker is absent) and allocating a blob container private to each test.
/// </summary>
/// <remarks>
/// The <c>Integration</c> category is declared here and inherited by every derived class, so the
/// category cannot drift out of sync with what is actually an integration test.
/// </remarks>
[TestCategory("Integration")]
public abstract class AzuriteIntegrationTestBase
{
    /// <summary>
    /// Gets the connection string for the shared Azurite instance.
    /// </summary>
    protected string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Gets a blob container name unique to the currently running test. Isolation is per test rather
    /// than per class because the assembly runs tests in parallel at method level against one
    /// container instance.
    /// </summary>
    protected string ContainerName { get; private set; } = null!;

    /// <summary>
    /// Acquires the shared Azurite endpoint and a fresh container name for this test.
    /// </summary>
    /// <returns>A task that completes once the endpoint is available.</returns>
    [TestInitialize]
    public async Task InitializeAzuriteAsync()
    {
        this.ConnectionString = await AzuriteFixture.ConnectionStringAsync();
        this.ContainerName = AzuriteFixture.NewContainerName();
    }

    /// <summary>
    /// Builds a capture-enabled storage configuration pointed at this test's container.
    /// </summary>
    /// <param name="configure">Optional further configuration.</param>
    /// <returns>The configuration.</returns>
    /// <remarks>
    /// The defaults here are chosen so no real-clock timer can influence a test:
    /// <c>BatchTimeoutSeconds</c> is far longer than any test runs (the batch pipeline's flush timer
    /// is a raw <see cref="System.Threading.Timer"/> that a fake clock cannot drive, so tests must
    /// flush explicitly), and <c>WriteRetryAttempts</c> of 1 disables Polly's retry entirely, whose
    /// delays also come from the real clock.
    /// </remarks>
    protected StorageConfig CreateStorageConfig(Action<StorageConfig>? configure = null)
    {
        StorageConfig config = new()
        {
            ConnectionString = this.ConnectionString,
            ContainerName = this.ContainerName,
            EnableCapture = true,
            BatchTimeoutSeconds = 300,
            WriteRetryAttempts = 1,
        };

        configure?.Invoke(config);
        return config;
    }

    /// <summary>
    /// Creates a blob container client used to verify what the code under test actually wrote. This
    /// deliberately bypasses the production client so assertions cannot be satisfied by a bug shared
    /// with the write path.
    /// </summary>
    /// <returns>A container client for this test's container.</returns>
    protected BlobContainerClient CreateVerificationClient() => new(this.ConnectionString, this.ContainerName);

    /// <summary>
    /// Downloads a blob's raw bytes.
    /// </summary>
    /// <param name="path">The blob path.</param>
    /// <returns>The bytes.</returns>
    protected async Task<byte[]> DownloadBytesAsync(string path) =>
        (await this.CreateVerificationClient().GetBlobClient(path).DownloadContentAsync()).Value.Content.ToArray();

    /// <summary>
    /// Downloads a blob and decodes it as ASCII, which is the encoding NMEA sentences are written in.
    /// </summary>
    /// <param name="path">The blob path.</param>
    /// <returns>The text.</returns>
    protected async Task<string> DownloadTextAsync(string path) =>
        Encoding.ASCII.GetString(await this.DownloadBytesAsync(path));

    /// <summary>
    /// Lists the blob paths present in this test's container, or an empty list when the container was
    /// never created.
    /// </summary>
    /// <returns>The blob paths, in the order the service returns them.</returns>
    protected async Task<IReadOnlyList<string>> ListBlobPathsAsync()
    {
        BlobContainerClient client = this.CreateVerificationClient();

        if (!await client.ExistsAsync())
        {
            return [];
        }

        List<string> paths = [];
        await foreach (BlobItem blob in client.GetBlobsAsync())
        {
            paths.Add(blob.Name);
        }

        return paths;
    }

    /// <summary>
    /// Creates a <em>block</em> blob at the given path, which is how these tests make a subsequent
    /// append fail deterministically: appending to a block blob is a type conflict that the service
    /// rejects in a single round trip, and which the Azure SDK does not retry.
    /// </summary>
    /// <param name="path">The blob path to occupy.</param>
    /// <param name="content">The content to place there.</param>
    /// <returns>A task that completes when the blob exists.</returns>
    protected async Task PreCreateBlockBlobAsync(string path, string content)
    {
        BlobContainerClient client = this.CreateVerificationClient();
        await client.CreateIfNotExistsAsync();

        using MemoryStream stream = new(Encoding.ASCII.GetBytes(content));
        await client.GetBlobClient(path).UploadAsync(stream, overwrite: true);
    }

    /// <summary>
    /// Polls until a condition holds, failing the test if it never does.
    /// </summary>
    /// <param name="condition">The condition to await.</param>
    /// <param name="description">What is being waited for, used in the failure message.</param>
    /// <param name="timeout">How long to wait. Defaults to 10 seconds.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    /// <remarks>
    /// Only for genuinely out-of-band effects - a fire-and-forget dead-letter replay sweep, for
    /// instance. Anything that can be awaited directly should be.
    /// </remarks>
    protected static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string description,
        TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(10);
        DateTime deadline = DateTime.UtcNow + limit;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out after {limit.TotalSeconds:0.#}s waiting for {description}.");
    }
}
