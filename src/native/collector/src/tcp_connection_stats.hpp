#pragma once

/// @file
/// @brief Sampler for the rate of failed TCP connection attempts (Windows).
/// Reads MIB_TCPSTATS.dwAttemptFails (IPv4+IPv6) and returns the per-call delta to feed a Rate sensor.
/// Portable header; implementation is #if _WIN32 guarded.

#include <cstdint>
#include <optional>

namespace hsm::collector
{
#ifdef _WIN32
    /// Stateful sampler: call Sample() repeatedly. The first call seeds the baseline and returns
    /// std::nullopt; subsequent calls return the number of failed TCP connection attempts since the
    /// previous call (the combined IPv4+IPv6 dwAttemptFails delta). Returns std::nullopt on a counter
    /// reset/wrap, a read error, or a zero delta (a quiet host posts nothing — the rate window stays 0).
    class WindowsTcpFailureSampler
    {
    public:
        WindowsTcpFailureSampler() = default;

        std::optional<double> Sample();

    private:
        std::uint64_t prev_ = 0;
        bool have_baseline_ = false;
    };
#endif // _WIN32
} // namespace hsm::collector
