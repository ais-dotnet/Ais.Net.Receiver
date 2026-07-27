// <copyright file="StorageHealthCheckIntegrationTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;

using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Storage.Azure.Blob.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Tests <see cref="StorageHealthCheck"/> against a real blob endpoint. The interesting states depend
/// on whether the container actually exists, which is not something a substitute can tell you
/// convincingly.
/// </summary>
[TestClass]
public class StorageHealthCheckIntegrationTests : AzuriteIntegrationTestBase
{
    [TestMethod]
    public async Task CheckHealthAsync_BeforeAnythingIsWritten_IsDegraded()
    {
        StorageHealthCheck check = new(Options.Create(this.CreateStorageConfig()));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        // Degraded rather than Unhealthy: the endpoint is reachable and the container will be created
        // on first write, so this is "not ready yet", not "broken".
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["enabled"].ShouldBe(true);
        result.Data["containerExists"].ShouldBe(false);
        result.Data["containerName"].ShouldBe(this.ContainerName);
    }

    [TestMethod]
    public async Task CheckHealthAsync_AfterFirstWriteCreatesTheContainer_IsHealthy()
    {
        StorageConfig config = this.CreateStorageConfig();
        FakeTimeProvider time = new(new DateTimeOffset(2026, 7, 22, 17, 30, 0, TimeSpan.Zero));

        using (AzureAppendBlobStorageClient client = new(config, time))
        {
            await client.PersistAsync([Encoding.ASCII.GetBytes("!AIVDM,1,1,,A,HEALTH,0*00")]);
        }

        StorageHealthCheck check = new(Options.Create(config));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["containerExists"].ShouldBe(true);
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenCaptureIsDisabled_IsHealthyAndDoesNotTouchStorage()
    {
        // A deliberately unusable endpoint: with capture disabled the check must short-circuit before
        // any network call, so an unreachable account cannot make a capture-less deployment unhealthy.
        StorageConfig config = new()
        {
            EnableCapture = false,
            ConnectionString = "DefaultEndpointsProtocol=http;AccountName=nope;AccountKey=bm9wZQ==;BlobEndpoint=http://127.0.0.1:1/nope;",
            ContainerName = "does-not-matter",
        };

        StorageHealthCheck check = new(Options.Create(config));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["enabled"].ShouldBe(false);
        result.Data.ShouldNotContainKey("containerExists");
    }

    [TestMethod]
    public async Task CheckHealthAsync_WhenStorageRejectsTheRequest_IsUnhealthy()
    {
        // A container name the service rejects (upper case and underscores are not legal), against the
        // real endpoint. That yields a 400 in one round trip.
        //
        // The obvious alternative - pointing at an unreachable endpoint - is what this test originally
        // did, and it cost roughly 20 seconds: the Azure SDK treats a connection failure as transient
        // and retries it with exponential backoff, and StorageHealthCheck builds its BlobContainerClient
        // internally so there is no seam to turn that off. A rejected request is not retried, so this
        // exercises the same catch-all-to-Unhealthy path in milliseconds.
        StorageConfig config = this.CreateStorageConfig(c => c.ContainerName = "Invalid_Container_Name");

        StorageHealthCheck check = new(Options.Create(config));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Data.ShouldContainKey("error");
        result.Exception.ShouldNotBeNull();
    }
}
