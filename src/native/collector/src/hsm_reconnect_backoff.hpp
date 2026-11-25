#pragma once

#include <algorithm>
#include <cstdint>
#include <random>

// The wait before the next attempt to open a connection to the server, shared by every connection
// that reconnects on its own: the collector session (#1668) and a second connection tied to it (the
// table connection of #1848). One formula, so that all of them back off alike. Internal, NOT part of
// the C ABI. Needs nothing but the standard library.
namespace hsm::http
{
    // Exponential: 1 s, 2 s, 4 s, ... doubling with each failed attempt up to 60 s.
    constexpr int64_t kMinReconnectBackoffMs = 1000;
    constexpr int64_t kMaxReconnectBackoffMs = 60000;

    // The step for `attempt` (0 for the first retry). The shift stops at 10 so it cannot overflow;
    // the step is at its 60 s cap well before that.
    inline int64_t ReconnectBackoffStepMs(int attempt)
    {
        const int shift = (std::min)((std::max)(attempt, 0), 10);
        return (std::min)(kMaxReconnectBackoffMs, kMinReconnectBackoffMs << shift);
    }

    // The wait itself, with jitter: between half of the step and the whole step, so a fleet that lost
    // its server together does not come back together. `random` is the caller's engine, which makes
    // the value reproducible in a test.
    template <class Engine>
    int64_t ReconnectBackoffDelayMs(int attempt, Engine& random)
    {
        const int64_t step = ReconnectBackoffStepMs(attempt);
        return std::uniform_int_distribution<int64_t>(step / 2, step)(random);
    }

    // The same with an engine of the calling thread, seeded once from std::random_device.
    inline int64_t ReconnectBackoffDelayMs(int attempt)
    {
        thread_local std::mt19937_64 random{ std::random_device{}() };
        return ReconnectBackoffDelayMs(attempt, random);
    }
} // namespace hsm::http
