#pragma once

// Internal: per-process CPU sampling for the "Top CPU" sensors (#1179).

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace hsm::collector
{
    struct CpuUsage
    {
        std::string name;      ///< executable image name, e.g. "chrome.exe"
        double      percent;   ///< CPU% of the whole machine (0..100), summed over all instances
        std::string full_path; ///< full path to the image, e.g. "C:\...\chrome.exe" (empty if access denied)
    };

    /// Filter out names below `min_percent`, return the `count` busiest descending by percent
    /// (deterministic tie-break by name ascending). `count <= 0` returns empty.
    std::vector<CpuUsage> SelectTopN(const std::map<std::string, CpuUsage>& by_name, int count, double min_percent);

#ifdef _WIN32
    /// Samples per-process CPU% (of the whole machine) between successive Sample() calls.
    /// Stateful: the first call seeds the baseline and returns an empty map.
    class WindowsCpuSampler
    {
    public:
        WindowsCpuSampler();
        std::map<std::string, CpuUsage> Sample(); ///< name -> {name, percent, full_path}

    private:
        struct PrevSample
        {
            std::uint64_t creation_100ns; ///< process creation time — detects PID reuse between ticks
            std::uint64_t cpu_100ns;      ///< cumulative (kernel+user) CPU time
        };

        std::map<std::uint32_t, PrevSample> prev_;
        std::uint64_t prev_wall_ms_ = 0;
        unsigned int cores_ = 1;
        bool have_baseline_ = false;
    };
#endif
} // namespace hsm::collector
