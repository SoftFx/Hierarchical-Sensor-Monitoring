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
        // One monotonic baseline PER address family. Summing both families into a single baseline
        // (the previous design) let an intermittently-unreadable family poison the shared baseline:
        // a family dropping out then returning would read as a counter reset, dumping its whole
        // cumulative count as one spurious delta (false spike) or silently dropping real failures.
        struct FamilyBaseline
        {
            std::uint64_t prev = 0;
            bool have_baseline = false;
        };

        FamilyBaseline v4_;
        FamilyBaseline v6_;
    };
#endif // _WIN32
} // namespace hsm::collector
