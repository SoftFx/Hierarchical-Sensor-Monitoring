#pragma once

#include <cstdint>

// Native mirror of the .NET resilience pipelines
// (src/collector/HSMDataCollector/Client/HttpsClient/RequestHandlers/BaseHandlers.cs +
//  DataHandlers.cs + CommandHandlers.cs). Pure arithmetic, no libcurl — compiled into the
// always-on core so the retry decision + the backoff schedule are unit-tested in the /WX lane.
//
// Two pipeline shapes, exactly as Polly is configured on the managed side:
//   * Bounded (data / priority / file):   MaxRetryAttempts = 10,           Exponential backoff.
//   * Unbounded (commands / addOrUpdate): MaxRetryAttempts = int.MaxValue, Linear backoff.
// Both use Delay = 1s and MaxDelay = 2min.
//
// ShouldRetry mirrors the #1096 BaseHandlers.ShouldRetry fix:
//   * a transport-level failure (connect refused / timeout / cancelled == a thrown exception
//     on the managed side) is always retried;
//   * a non-success HTTP result is retried ONLY for 5xx AND only on the bounded pipelines —
//     a persistent 5xx must not hang the unbounded command pipeline forever;
//   * 4xx are permanent and never retried.
namespace hsm::http
{
    enum class BackoffType
    {
        Exponential, // bounded data/priority/file pipelines
        Linear,      // unbounded command pipeline
    };

    struct RetryPolicy
    {
        int32_t max_attempts = 10; // int.MaxValue (or <0) == unbounded
        BackoffType backoff = BackoffType::Exponential;
        int64_t base_delay_ms = 1000;  // Polly Delay = 1s
        int64_t max_delay_ms = 120000; // Polly MaxDelay = 2min

        bool IsBounded() const { return max_attempts >= 0 && max_attempts != INT32_MAX; }

        // Bounded data/priority/file pipeline.
        static RetryPolicy Data()
        {
            return RetryPolicy{ 10, BackoffType::Exponential, 1000, 120000 };
        }

        // Unbounded command/addOrUpdate pipeline.
        static RetryPolicy Commands()
        {
            return RetryPolicy{ INT32_MAX, BackoffType::Linear, 1000, 120000 };
        }

        // Decide whether to retry after one attempt. transport_ok == false models a thrown
        // exception (always retryable); otherwise the HTTP status decides.
        bool ShouldRetry(bool transport_ok, int64_t status_code) const
        {
            if (!transport_ok)
                return true;

            const bool is_success = status_code >= 200 && status_code < 300;
            if (is_success)
                return false;

            const bool is_server_error = status_code >= 500;
            return is_server_error && IsBounded();
        }

        // Whether another attempt is allowed given how many have already run (1-based count).
        bool HasAttemptsLeft(int32_t attempts_made) const
        {
            if (!IsBounded())
                return true;
            // MaxRetryAttempts is the number of RETRIES on top of the first try.
            return attempts_made <= max_attempts;
        }

        // Backoff before the (retry_index)-th retry, retry_index 0-based (delay before retry #1
        // is DelayMs(0)). Polly v8: Exponential = base * 2^n, Linear = base * (n + 1); both
        // clamped to MaxDelay.
        int64_t DelayMs(int32_t retry_index) const
        {
            int64_t delay;
            if (backoff == BackoffType::Exponential)
            {
                delay = base_delay_ms;
                for (int32_t i = 0; i < retry_index && delay < max_delay_ms; ++i)
                    delay *= 2;
            }
            else
            {
                delay = base_delay_ms * (static_cast<int64_t>(retry_index) + 1);
            }

            return delay > max_delay_ms ? max_delay_ms : delay;
        }
    };
} // namespace hsm::http
