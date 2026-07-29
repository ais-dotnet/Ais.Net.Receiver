// <copyright file="RetryPipelines.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Polly;
using Polly.Retry;

namespace Ais.Net.Receiver.Resilience;

/// <summary>
/// Factory for the Polly resilience pipelines used across the receiver.
/// </summary>
internal static class RetryPipelines
{
    /// <summary>
    /// Builds a retry pipeline that retries any exception with a constant delay, for up to
    /// <paramref name="maxAttempts"/> total attempts (the initial try plus retries).
    /// </summary>
    /// <param name="delay">The constant delay between attempts.</param>
    /// <param name="maxAttempts">The maximum number of attempts (initial try plus retries).</param>
    /// <returns>The resilience pipeline.</returns>
    public static ResiliencePipeline ConstantDelay(TimeSpan delay, int maxAttempts)
    {
        // Polly requires MaxRetryAttempts >= 1; a single total attempt means no retry strategy at all.
        if (maxAttempts <= 1)
        {
            return ResiliencePipeline.Empty;
        }

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = maxAttempts - 1, // Polly counts retries; we count total attempts
                Delay = delay,
                BackoffType = DelayBackoffType.Constant,
            })
            .Build();
    }
}
