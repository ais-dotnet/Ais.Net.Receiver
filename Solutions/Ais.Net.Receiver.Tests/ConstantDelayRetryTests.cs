// <copyright file="ConstantDelayRetryTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;

using Ais.Net.Receiver.Resilience;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ConstantDelayRetryTests
{
    private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(1);

    [TestMethod]
    public async Task ExecuteAsync_WhenOperationSucceeds_RunsOnce()
    {
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 3);
        int calls = 0;

        await retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        calls.ShouldBe(1);
    }

    [TestMethod]
    public async Task ExecuteAsync_RetriesUntilSuccess()
    {
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 3);
        int calls = 0;

        await retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return calls < 3 ? Task.FromException(new IOException("transient")) : Task.CompletedTask;
            },
            CancellationToken.None);

        calls.ShouldBe(3);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenAttemptsExhausted_ThrowsLastExceptionAfterMaxAttempts()
    {
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 3);
        int calls = 0;

        IOException thrown = await Should.ThrowAsync<IOException>(() => retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.FromException(new IOException($"attempt {calls}"));
            },
            CancellationToken.None));

        // maxAttempts counts the initial try, and the exception from the final attempt is surfaced.
        calls.ShouldBe(3);
        thrown.Message.ShouldBe("attempt 3");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithSingleAttempt_DoesNotRetry()
    {
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 1);
        int calls = 0;

        await Should.ThrowAsync<IOException>(() => retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.FromException(new IOException("down"));
            },
            CancellationToken.None));

        calls.ShouldBe(1);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCancelledDuringDelay_ThrowsOperationCanceledPromptly()
    {
        // A long delay that a shutdown must not have to sit through.
        ConstantDelayRetry retry = new(TimeSpan.FromSeconds(30), maxAttempts: 5);
        int calls = 0;
        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        Stopwatch stopwatch = Stopwatch.StartNew();
        await Should.ThrowAsync<OperationCanceledException>(() => retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.FromException(new IOException("transient"));
            },
            cts.Token));
        stopwatch.Stop();

        calls.ShouldBe(1);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenOperationCancelledWithCallerToken_DoesNotRetry()
    {
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 5);
        int calls = 0;
        using CancellationTokenSource cts = new();

        await Should.ThrowAsync<OperationCanceledException>(() => retry.ExecuteAsync(
            token =>
            {
                calls++;
                cts.Cancel();
                return Task.FromCanceled(token);
            },
            cts.Token));

        calls.ShouldBe(1);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenOperationThrowsCancellationForAnotherToken_Retries()
    {
        // A cancellation that is not the caller's (e.g. an internal timeout) is a transient failure.
        ConstantDelayRetry retry = new(FastRetry, maxAttempts: 3);
        int calls = 0;

        await retry.ExecuteAsync(
            _ =>
            {
                calls++;
                return calls < 2
                    ? Task.FromException(new OperationCanceledException("timeout", new CancellationToken(canceled: true)))
                    : Task.CompletedTask;
            },
            CancellationToken.None);

        calls.ShouldBe(2);
    }
}
