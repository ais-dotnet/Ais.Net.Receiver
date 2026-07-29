// <copyright file="AzuriteFixture.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Testcontainers.Azurite;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Owns the single Azurite container shared by every integration test in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// The container is started lazily on first use rather than from <c>[AssemblyInitialize]</c>, so a
/// unit-only run never pays the container cost. <see cref="Lazy{T}"/> over a <see cref="Task{T}"/>
/// gives exactly-once startup under the assembly's method-level parallelism, and every waiting test
/// observes the same result - or the same cached exception.
/// </para>
/// <para>
/// Startup failures are deliberately <em>not</em> collapsed into a single "skipped" outcome. A missing
/// Docker endpoint is a legitimate reason to skip; a withdrawn image tag, a pull timeout or a port
/// conflict is a real failure, and reporting those as skips is how a suite comes to report green
/// having run no integration tests at all.
/// </para>
/// </remarks>
internal static class AzuriteFixture
{
    private const string DefaultImage = "mcr.microsoft.com/azure-storage/azurite:latest";
    private const int DefaultStartupTimeoutSeconds = 120;

    private static readonly Lazy<Task<string>> Endpoint = new(StartAsync);

    private static AzuriteContainer? container;

    /// <summary>
    /// Gets the connection string for the shared Azurite instance, starting it if necessary.
    /// </summary>
    /// <returns>The connection string.</returns>
    /// <remarks>
    /// Calls <see cref="Assert.Inconclusive(string)"/> when Docker is unavailable and integration
    /// tests are not required, and throws when Docker was reachable but Azurite would not start.
    /// </remarks>
    internal static Task<string> ConnectionStringAsync() => Endpoint.Value;

    /// <summary>
    /// Gets a blob container name unique to one test, so tests sharing the Azurite instance cannot
    /// observe each other's blobs.
    /// </summary>
    /// <returns>A fresh container name.</returns>
    internal static string NewContainerName() => "test-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Disposes the shared container, if one was ever started.
    /// </summary>
    /// <returns>A task that completes when the container has been disposed.</returns>
    internal static async Task ShutdownAsync()
    {
        // Only tear down what we actually created. Testcontainers' Ryuk sidecar is the backstop if
        // the test process dies before reaching this point.
        if (Endpoint.IsValueCreated && container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }
    }

    private static async Task<string> StartAsync()
    {
        bool required = IsTruthy(Environment.GetEnvironmentVariable("AIS_TEST_REQUIRE_INTEGRATION"));

        // Cheap pre-flight: with no endpoint configured and no socket or pipe on disk, Docker is
        // simply not installed here. Skip without paying a start attempt and its timeout.
        if (!required && !DockerEndpointLooksPresent())
        {
            Assert.Inconclusive(
                "Docker is not available (no DOCKER_HOST, and no Docker socket or pipe found), so the " +
                "Azurite integration tests were skipped. Set AIS_TEST_REQUIRE_INTEGRATION=true to fail " +
                "instead of skipping.");
        }

        string image = ImageRef();

        // The image goes through the constructor: the parameterless overload plus WithImage is
        // obsolete in Testcontainers 4.x.
        AzuriteBuilder builder = new(image);

        // Azurite trails the newest x-ms-version the Azure SDK sends, and answers an otherwise valid
        // request with 400 InvalidHeaderValue when it does. Skipping the check is the default so the
        // suite is not hostage to that release cadence; setting the variable falsy re-enables it,
        // which is how you deliberately surface an SDK/Azurite skew. WithCommand appends to
        // Azurite's own default arguments rather than replacing them.
        if (IsTruthy(Environment.GetEnvironmentVariable("AIS_TEST_AZURITE_SKIP_API_VERSION_CHECK") ?? "true"))
        {
            builder = builder.WithCommand("--skipApiVersionCheck");
        }

        AzuriteContainer azurite = builder.Build();

        using CancellationTokenSource cts = new(StartupTimeout());

        try
        {
            await azurite.StartAsync(cts.Token);
            container = azurite;
            return azurite.GetConnectionString();
        }
        catch (Exception ex)
        {
            await azurite.DisposeAsync();

            // The only skippable failure: Testcontainers could not find a usable Docker endpoint.
            if (!required && IsDockerEndpointUnavailable(ex))
            {
                Assert.Inconclusive(
                    "Docker is not available, so the Azurite integration tests were skipped: " +
                    ex.Message +
                    " Set AIS_TEST_REQUIRE_INTEGRATION=true to fail instead of skipping.");
            }

            // Everything else means Docker answered and Azurite still did not come up: a bad or
            // withdrawn image tag, a failed pull, a port conflict, or our own startup timeout. Those
            // are genuine failures and must not masquerade as "no Docker".
            throw new InvalidOperationException(
                $"A Docker endpoint was available but the Azurite container ('{image}') failed to " +
                "start. This is a genuine integration-test failure, not a missing-Docker skip. If " +
                "the ':latest' image has regressed, pin a known-good tag with AIS_TEST_AZURITE_IMAGE.",
                ex);
        }
    }

    private static string ImageRef() =>
        Environment.GetEnvironmentVariable("AIS_TEST_AZURITE_IMAGE") is { Length: > 0 } image
            ? image
            : DefaultImage;

    private static TimeSpan StartupTimeout() =>
        TimeSpan.FromSeconds(
            int.TryParse(
                Environment.GetEnvironmentVariable("AIS_TEST_AZURITE_STARTUP_TIMEOUT_SECONDS"),
                out int seconds) && seconds > 0
                ? seconds
                : DefaultStartupTimeoutSeconds);

    /// <summary>
    /// Distinguishes "no usable Docker endpoint" from every other startup failure.
    /// </summary>
    /// <remarks>
    /// Testcontainers reports an unreachable or unconfigured daemon as an <see cref="ArgumentException"/>
    /// whose message states that Docker is not running or is misconfigured. Every other exception -
    /// a Docker API error, an image that cannot be pulled, a wait-strategy timeout, a resource-reaper
    /// failure, or the cancellation of our own startup timeout - means the daemon responded and the
    /// container itself is at fault.
    /// </remarks>
    /// <param name="exception">The startup exception to classify.</param>
    /// <returns><see langword="true"/> when the failure is a missing Docker endpoint.</returns>
    internal static bool IsDockerEndpointUnavailable(Exception exception) =>
        exception is ArgumentException
        && exception.Message.Contains(
            "Docker is either not running or misconfigured",
            StringComparison.OrdinalIgnoreCase);

    private static bool DockerEndpointLooksPresent()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"\\.\pipe\docker_engine");
        }

        if (File.Exists("/var/run/docker.sock"))
        {
            return true;
        }

        // Rootless and Docker Desktop on macOS put the socket under the user profile.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return File.Exists(Path.Combine(home, ".docker", "run", "docker.sock"))
            || File.Exists(Path.Combine(home, ".docker", "desktop", "docker.sock"));
    }

    private static bool IsTruthy(string? value) =>
        value is not null
        && value.Trim() is { Length: > 0 } trimmed
        && !trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !trimmed.Equals("0", StringComparison.Ordinal)
        && !trimmed.Equals("no", StringComparison.OrdinalIgnoreCase)
        && !trimmed.Equals("off", StringComparison.OrdinalIgnoreCase);
}
