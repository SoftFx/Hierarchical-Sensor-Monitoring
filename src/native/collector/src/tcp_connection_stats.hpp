#pragma once

/// @file
/// @brief Sampler for the rate of failed TCP connection attempts (Windows).
/// Reads MIB_TCPSTATS.dwAttemptFails (IPv4+IPv6) and returns the per-call delta to feed a Rate sensor.
/// Portable header; the OS read is #if _WIN32 guarded, the baseline/delta math and the sampler seam
/// (family readers in, error report out) are portable.

#include <cstdint>
#include <functional>
#include <optional>
#include <string>

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

    /// What the sampler reports when the TCP statistics of neither address family can be read. The
    /// managed collector (WindowsTcpFailureRateMonitor) reports the same text for the same condition.
    inline constexpr const char* kTcpNeitherFamilyReadableError =
        "TCP connection failure rate sampling error: the TCP statistics of neither IPv4 nor IPv6 could be read.";

    /// Portable sampler: one tick reads each address family through an injected reader and feeds the
    /// accumulator. This is the seam the unit tests drive; WindowsTcpFailureSampler supplies the OS
    /// readers.
    ///
    /// A tick on which NEITHER family can be read is reported through `report_error`: nothing stands
    /// behind the 0 the rate sensor goes on posting, and that must not pass for a quiet network (root
    /// rule 8). One unreadable family sits the tick out quietly (IPv6 not installed, for one). The
    /// report is made on every such tick, the seeding one included; collapsing a run of them to one
    /// line per window is the job of the collector's deduplicated error channel behind the callback,
    /// as it is for the managed monitor.
    ///
    /// Not thread-safe: one thread samples.
    class TcpFailureRateSampler
    {
    public:
        using FamilyReading = TcpFailureRateAccumulator::FamilyReading;
        using FamilyReader = std::function<FamilyReading()>;
        using ErrorReporter = std::function<void(const std::string&)>;

        /// The readers must be callable; an empty `report_error` reports nowhere.
        TcpFailureRateSampler(FamilyReader read_v4, FamilyReader read_v6, ErrorReporter report_error);

        /// The first call seeds the baselines and returns std::nullopt; later calls return the failed
        /// connection attempts since the previous call (IPv4+IPv6), or std::nullopt when there is
        /// nothing to post (see TcpFailureRateAccumulator::Accumulate).
        std::optional<double> Sample();

    private:
        FamilyReader read_v4_;
        FamilyReader read_v6_;
        ErrorReporter report_error_;
        TcpFailureRateAccumulator accumulator_;
    };

#ifdef _WIN32
    /// Stateful sampler: call Sample() repeatedly. The first call seeds the baseline and returns
    /// std::nullopt; subsequent calls return the number of failed TCP connection attempts since the
    /// previous call (the combined IPv4+IPv6 dwAttemptFails delta). Returns std::nullopt on a counter
    /// reset/wrap, a read error, or a zero delta (a quiet host posts nothing — the rate window stays 0).
    /// A thin GetTcpStatisticsEx binding of TcpFailureRateSampler: a tick on which neither family
    /// could be read is reported through `report_error`.
    class WindowsTcpFailureSampler
    {
    public:
        explicit WindowsTcpFailureSampler(TcpFailureRateSampler::ErrorReporter report_error);

        std::optional<double> Sample();

    private:
        TcpFailureRateSampler sampler_;
    };
#endif // _WIN32
} // namespace hsm::collector
