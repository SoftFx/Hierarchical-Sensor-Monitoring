#pragma once

/// @file
/// @brief Sampler for the rate of failed TCP connection attempts (Windows).
/// Reads MIB_TCPSTATS.dwAttemptFails (IPv4+IPv6) and returns the per-call delta to feed a Rate sensor.
/// Portable header; the OS read is #if _WIN32 guarded, the baseline/delta math is portable.

#include <cstdint>
#include <optional>

namespace hsm::collector
{
    /// Portable, OS-free accumulator for the failed-TCP-attempt rate. Fed the raw cumulative
    /// dwAttemptFails reading per address family each tick, it returns the combined IPv4+IPv6 delta to
    /// push into the Rate sensor, or std::nullopt when nothing should be posted. The baseline/delta/
    /// wrap/quiet-interval logic lives here (not inside the Windows sampler) so it is unit-testable
    /// without the OS — WindowsTcpFailureSampler is a thin GetTcpStatisticsEx wrapper over it.
    class TcpFailureRateAccumulator
    {
    public:
        /// One address family's reading for a single tick. @c readable is false when the OS read
        /// failed for that family (e.g. GetTcpStatisticsEx != NO_ERROR): the family contributes 0 and
        /// keeps its baseline, so it resumes cleanly without a false spike.
        struct FamilyReading
        {
            bool readable = false;
            std::uint64_t attempt_fails = 0;
        };

        /// Returns the combined per-tick delta since the previous readable tick, or std::nullopt when:
        /// neither family is readable, a family's first reading (baseline seed), a counter reset/wrap
        /// with no other progress, or a zero delta (a quiet host posts nothing — the rate stays 0).
        std::optional<double> Accumulate(const FamilyReading& v4, const FamilyReading& v6);

    private:
        // One monotonic baseline PER address family. Summing both families into a single baseline
        // (an earlier design) let an intermittently-unreadable family poison the shared baseline:
        // a family dropping out then returning would read as a counter reset, dumping its whole
        // cumulative count as one spurious delta (false spike) or silently dropping real failures.
        struct FamilyBaseline
        {
            std::uint64_t prev = 0;
            bool have_baseline = false;
        };

        static void AccumulateFamily(FamilyBaseline& family, const FamilyReading& reading,
                                     bool& any_readable, std::uint64_t& total_delta);

        FamilyBaseline v4_;
        FamilyBaseline v6_;
    };

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
        TcpFailureRateAccumulator accumulator_;
    };
#endif // _WIN32
} // namespace hsm::collector
