#pragma once

/// @file
/// @brief Per-interface network speed sampler for Windows.
/// Samples GetIfTable2 cumulative octet counters, computes MB/sec deltas.
/// Portable header; implementation is #if _WIN32 guarded.

#include <map>
#include <string>

namespace hsm::collector
{
    /// One interface's speed sample (MB/sec since last call).
    struct InterfaceSpeed
    {
        std::string name;        // Interface alias (UTF-8), matches .NET NetworkInterface.Name
        double rx_mb_per_sec;   // Bytes received / elapsed / 1 MiB
        double tx_mb_per_sec;   // Bytes sent    / elapsed / 1 MiB
    };

#ifdef _WIN32
    /// Stateful sampler: call Sample() repeatedly; the first call seeds the baseline and returns
    /// an empty map. Subsequent calls return one entry per active non-loopback interface whose
    /// counter delta is non-negative (negative delta = counter reset, skipped).
    class WindowsNetworkSampler
    {
    public:
        WindowsNetworkSampler() = default;

        /// Sample all active (OperStatus==Up, non-loopback) interfaces.
        /// Returns empty on the first call (baseline seeding); subsequent calls return deltas.
        std::map<std::string, InterfaceSpeed> Sample();

    private:
        struct PrevEntry
        {
            std::uint64_t rx_octets = 0;
            std::uint64_t tx_octets = 0;
        };

        std::map<std::string, PrevEntry> prev_;
        std::uint64_t prev_wall_ms_ = 0;
        bool have_baseline_ = false;
    };
#endif // _WIN32

} // namespace hsm::collector
