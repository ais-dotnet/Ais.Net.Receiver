// <copyright file="NotCancelledRetryPolicy.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Corvus.Retry.Policies;

namespace Ais.Net.Receiver.Resilience;

/// <summary>
/// Retries any failure except cancellation of the caller's token: the behaviour Polly's retry
/// strategy had with <c>Handle&lt;Exception&gt;()</c> plus its own cancellation checks.
/// </summary>
/// <remarks>
/// Corvus.Retry's loop stops retrying once the token is cancelled, but it only consults the token
/// after an attempt has failed. This policy additionally refuses to retry an
/// <see cref="OperationCanceledException"/> raised for that same token, so a cancelled wait or a
/// cancelled operation is surfaced to the caller rather than counted as a transient failure. A
/// cancellation carrying a different token (for example an internal timeout) is still retried,
/// as it was under Polly.
/// </remarks>
internal sealed class NotCancelledRetryPolicy : IRetryPolicy
{
    private readonly CancellationToken cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotCancelledRetryPolicy"/> class.
    /// </summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    public NotCancelledRetryPolicy(CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public bool CanRetry(Exception exception)
    {
        if (this.cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is not OperationCanceledException cancelled
            || cancelled.CancellationToken != this.cancellationToken;
    }
}
