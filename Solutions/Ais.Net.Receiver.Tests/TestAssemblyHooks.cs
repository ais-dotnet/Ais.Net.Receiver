// <copyright file="TestAssemblyHooks.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Assembly-wide test hooks. MSTest requires <c>[AssemblyCleanup]</c> to live on a
/// <c>[TestClass]</c>, so it sits here rather than forcing <see cref="AzuriteFixture"/> to be one.
/// </summary>
[TestClass]
public sealed class TestAssemblyHooks
{
    /// <summary>
    /// Tears down the shared Azurite container once every test has finished. No-op when no
    /// integration test ran, since the container is only started on demand.
    /// </summary>
    /// <returns>A task that completes when cleanup has finished.</returns>
    [AssemblyCleanup]
    public static async Task CleanupAsync() => await AzuriteFixture.ShutdownAsync();
}
