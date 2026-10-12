// <copyright file="AzuriteFixtureTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Docker.DotNet;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Unit tests for <see cref="AzuriteFixture"/>'s skip-versus-fail classification.
/// </summary>
/// <remarks>
/// This decision is the difference between a suite that honestly skips when Docker is absent and one
/// that silently reports green when the container is broken, so it is worth testing directly. It also
/// cannot be covered by running the integration tests: simulating an absent Docker daemon on a machine
/// that has one is unreliable, because Testcontainers falls back past an unusable
/// <c>DOCKER_HOST</c> to its other endpoint-discovery strategies.
/// </remarks>
[TestClass]
public class AzuriteFixtureTests
{
    [TestMethod]
    public void IsDockerEndpointUnavailable_ForTestcontainersMisconfiguredDockerMessage_IsSkippable()
    {
        // The message Testcontainers raises when it cannot find a usable endpoint at all.
        ArgumentException exception = new(
            "Docker is either not running or misconfigured. Please ensure that Docker is running.");

        AzuriteFixture.IsDockerEndpointUnavailable(exception).ShouldBeTrue();
    }

    [TestMethod]
    public void IsDockerEndpointUnavailable_ForImagePullFailure_IsNotSkippable()
    {
        // A withdrawn or mistyped tag: Docker answered, so this is a real failure. Reporting it as a
        // skip is how a suite comes to run no integration tests while still reporting success.
        DockerApiException exception = new(
            System.Net.HttpStatusCode.NotFound,
            "failed to resolve reference \"azurite:0.0.0\": not found");

        AzuriteFixture.IsDockerEndpointUnavailable(exception).ShouldBeFalse();
    }

    [TestMethod]
    public void IsDockerEndpointUnavailable_ForWaitStrategyTimeout_IsNotSkippable()
    {
        AzuriteFixture.IsDockerEndpointUnavailable(new TimeoutException("Wait strategy timed out."))
            .ShouldBeFalse();
    }

    [TestMethod]
    public void IsDockerEndpointUnavailable_ForOurStartupTimeoutCancellation_IsNotSkippable()
    {
        // AIS_TEST_AZURITE_STARTUP_TIMEOUT_SECONDS elapsing means the pull or start hung, not that
        // Docker is missing.
        AzuriteFixture.IsDockerEndpointUnavailable(new OperationCanceledException())
            .ShouldBeFalse();
    }

    [TestMethod]
    public void IsDockerEndpointUnavailable_ForAnUnrelatedArgumentException_IsNotSkippable()
    {
        // Right exception type, wrong reason: only the specific "not running or misconfigured" message
        // indicates a missing endpoint.
        AzuriteFixture.IsDockerEndpointUnavailable(new ArgumentException("Port already allocated."))
            .ShouldBeFalse();
    }
}
