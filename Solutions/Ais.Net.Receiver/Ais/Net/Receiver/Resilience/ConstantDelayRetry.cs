// <copyright file="ConstantDelayRetry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Corvus.Retry;
using Corvus.Retry.Strategies;

namespace Ais.Net.Receiver.Resilience;

/// <summary>
/// Retries an operation on any failure with a constant delay between attempts, for up to a total
/// number of attempts (the initial try plus retries). Built on Corvus.Retry.
/// </summary>
/// <remarks>
/// <para>
/// Corvus.Retry sleeps between attempts through a process-wide sleep service that cannot observe a
/// cancellation token, so a shutdown requested during the wait would not be seen until the wait
/// ended. The strategy used here therefore reports no delay, and the wait is taken at the start of
/// each retry attempt with <see cref="Task.Delay(TimeSpan, CancellationToken)"/> instead. The
/// <see cref="OperationCanceledException"/> that produces on cancellation is rejected by
/// <see cref="NotCancelledRetryPolicy"/> and surfaces to the caller, as it did under Polly.
/// </para>
/// <para>
/// Corvus.Retry strategies carry per-run state (the attempt count), so a fresh strategy and policy
/// are created for every execution; this type itself is immutable and safe to share.
/// </para>
/// </remarks>
internal sealed class ConstantDelayRetry
{
    private readonly TimeSpan delay;
    private readonly int maxAttempts;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstantDelayRetry"/> class.
    /// </summary>
    /// <param name="delay">The constant delay between attempts.</param>
    /// <param name="maxAttempts">
    /// The maximum number of attempts (initial try plus retries). Values below 1 mean a single
    /// attempt with no retry.
    /// </param>
    public ConstantDelayRetry(TimeSpan delay, int maxAttempts)
    {
        this.delay = delay;
        this.maxAttempts = Math.Max(1, maxAttempts);
    }

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying on failure until it succeeds, the attempts are
    /// exhausted, or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="operation">The operation to run; receives the caller's token.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes when the operation has succeeded.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled during an attempt or a wait.</exception>
    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        int attempt = 0;

        return Retriable.RetryAsync(
            async () =>
            {
                if (attempt++ > 0 && this.delay > TimeSpan.Zero)
                {
                    await Task.Delay(this.delay, cancellationToken).ConfigureAwait(false);
                }

                await operation(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            new Count(this.maxAttempts),
            new NotCancelledRetryPolicy(cancellationToken),
            false);
    }
}
