#pragma once

/// @file
/// @brief Portable parsers + delta math for the Linux `/proc` metric sources (#1414).
///
/// Mirrors the managed Unix reference implementations algorithm-for-algorithm (repo rule #10 —
/// one sensor, one acquisition mechanism):
///   - ProcStat / ProcStatCpuUsage  <-> DefaultSensors/Unix/SystemInfo/ProcStat.cs
///   - ParseMeminfoAvailableKb      <-> DefaultSensors/Unix/SystemInfo/ProcMeminfo.cs (ParseAvailableKb)
///   - ParseProcSelfStat            <-> what .NET's Process reads on Linux for TotalProcessorTime /
///                                     WorkingSet64 (the `utime`/`stime`/`rss` fields of /proc/<pid>/stat)
///   - ProcessCpuUsage              <-> DefaultSensors/Unix/Process/UnixProcessCPU.cs
///
/// Everything here is OS-read-free so it is unit-testable on any platform (the precedent is
/// tcp_connection_stats.hpp: portable accumulator + `#if _WIN32` OS read). The actual `/proc`
/// reads live in platform/hsm_linux_metric_sources.cpp behind `#if defined(__linux__)`.

#include <cstdint>
#include <optional>
#include <string>

namespace hsm::collector
{
    /// Aggregate CPU times parsed from the first ("cpu ") line of /proc/stat.
    /// Idle is the `idle` field ONLY — `iowait` is deliberately counted as busy time, matching the
    /// managed CpuTimes (and the `top -bn1 ... 100 - id` sensor it replaced, where top's `id`
    /// excludes iowait). Total is the sum of every reported field EXCEPT `guest`/`guest_nice`,
    /// which Linux already folds into `user`/`nice`.
    struct ProcStatCpuTimes
    {
        double idle = 0.0;
        double total = 0.0;
    };

    /// Parses the aggregate CPU line of /proc/stat. Returns std::nullopt when the content is empty,
    /// the first line is not the aggregate `cpu` line (a per-core `cpu0` line must NOT match), it
    /// carries fewer fields than `idle`, or any field is not an integer.
    std::optional<ProcStatCpuTimes> ParseProcStatCpuTimes(const std::string& proc_stat_content);

    /// Computes total CPU busy percentage from successive /proc/stat samples. Holds the previous
    /// sample and returns the busy fraction over the interval since the last call. Not thread-safe;
    /// each source owns its own instance and samples it from one collect loop.
    class ProcStatCpuUsage
    {
    public:
        /// Seeds the baseline so the first collected value measures usage since construction, not
        /// since boot (mirrors UnixTotalCpu's constructor reading /proc/stat).
        explicit ProcStatCpuUsage(const std::string& initial_proc_stat_content);

        /// Busy CPU percent (0..100) over the interval since the previous sample, or std::nullopt
        /// when there is no usable baseline, the sample is unparseable, or no time elapsed
        /// (identical samples / a counter reset, i.e. totalDelta <= 0).
        std::optional<double> NextBusyPercent(const std::string& proc_stat_content);

    private:
        std::optional<ProcStatCpuTimes> previous_;
    };

    /// Available memory in kB from /proc/meminfo. Prefers `MemAvailable`; when an older/custom
    /// kernel omits it, estimates `MemFree + Buffers + Cached + SReclaimable - Shmem` (clamped at
    /// 0). Returns std::nullopt when none of those fields is present or parseable.
    std::optional<std::int64_t> ParseMeminfoAvailableKb(const std::string& meminfo_content);

    /// The fields of /proc/<pid>/stat the managed sensors observe through .NET's Process on Linux.
    struct ProcSelfStat
    {
        std::uint64_t utime_ticks = 0; ///< field 14 — user time in clock ticks
        std::uint64_t stime_ticks = 0; ///< field 15 — kernel time in clock ticks
        std::int64_t rss_pages = 0;    ///< field 24 — resident set size in pages (what WorkingSet64 reads)
    };

    /// Parses /proc/<pid>/stat. The executable name (field 2) is wrapped in parentheses and may
    /// itself contain spaces and parentheses, so field splitting starts after the LAST ')'.
    /// Returns std::nullopt on empty/short/malformed content.
    std::optional<ProcSelfStat> ParseProcSelfStat(const std::string& stat_content);

    /// Process CPU percent over the interval between two samples, mirroring UnixProcessCpu:
    /// `(cpuUsedMs / totalMsPassed) * 100`, where cpuUsedMs is the (utime+stime) delta and
    /// totalMsPassed is the wall-clock delta. NOT normalized by core count (a 2-core-saturating
    /// process reads 200%), and NOT clamped — exactly what the managed sensor posts.
    class ProcessCpuUsage
    {
    public:
        /// @param clock_ticks_per_second sysconf(_SC_CLK_TCK) — how many stat ticks make a second.
        explicit ProcessCpuUsage(double clock_ticks_per_second);

        /// Feeds one sample. Returns std::nullopt for the first sample (baseline only) and when no
        /// wall time elapsed since the previous one (a division by zero otherwise).
        std::optional<double> NextCpuPercent(std::uint64_t cpu_ticks, std::int64_t wall_clock_ms);

    private:
        double ticks_per_second_;
        std::uint64_t previous_cpu_ticks_ = 0;
        std::int64_t previous_wall_ms_ = 0;
        bool has_previous_ = false;
    };
} // namespace hsm::collector
