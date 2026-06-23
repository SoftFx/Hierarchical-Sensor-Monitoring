#pragma once

/// @file
/// @brief "Top processes by CPU" sampling for the agent (issue #1175).
///
/// Two layers, deliberately split so the selection logic is unit-tested without Win32:
///   * `SelectTopN` — pure: given per-exe aggregated CPU%, filter by a minimum and return the
///     busiest `count` names, descending (deterministic tie-break by name). Built on every platform.
///   * `WindowsCpuSampler` — Win32 only: enumerates processes, diffs CPU time against the previous
///     call, and returns CPU% (of the whole machine) aggregated by exe name.

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace hsm::agent
{
    struct CpuUsage
    {
        std::string name; ///< executable image name, e.g. "chrome.exe"
        double percent;   ///< CPU% of the whole machine (0..100), summed over all instances
    };

    /// Filter out names below `min_percent`, then return the `count` busiest, descending by percent
    /// (ties broken by name ascending so the result is deterministic). `count <= 0` returns empty.
    std::vector<CpuUsage> SelectTopN(const std::map<std::string, double>& by_name, int count, double min_percent);

#ifdef _WIN32
    /// Samples per-process CPU between successive `Sample()` calls. Stateful: it remembers each PID's
    /// cumulative CPU time and the wall-clock of the previous call, so a value is a true delta over the
    /// elapsed interval. The FIRST call only seeds the baseline and returns an empty map.
    class WindowsCpuSampler
    {
    public:
        WindowsCpuSampler();

        /// Per-exe-name aggregated CPU% (of the whole machine) since the previous call. Processes that
        /// appeared or vanished between calls contribute nothing to this tick.
        std::map<std::string, double> Sample();

    private:
        std::map<std::uint32_t, std::uint64_t> prev_cpu_100ns_; ///< pid -> cumulative (kernel+user) 100ns
        std::uint64_t prev_wall_100ns_ = 0;
        unsigned int cores_ = 1;
        bool have_baseline_ = false;
    };
#endif
} // namespace hsm::agent
