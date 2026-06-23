#include "agent/cpu_top.hpp"

#include <algorithm>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <tlhelp32.h>
#endif

namespace hsm::agent
{
    std::vector<CpuUsage> SelectTopN(const std::map<std::string, double>& by_name, int count, double min_percent)
    {
        std::vector<CpuUsage> result;
        if (count <= 0)
            return result;

        for (const auto& entry : by_name)
            if (entry.second >= min_percent)
                result.push_back({ entry.first, entry.second });

        // Busiest first; deterministic tie-break by name so equal-CPU processes order stably.
        std::sort(result.begin(), result.end(), [](const CpuUsage& a, const CpuUsage& b) {
            if (a.percent != b.percent)
                return a.percent > b.percent;
            return a.name < b.name;
        });

        if (result.size() > static_cast<std::size_t>(count))
            result.resize(static_cast<std::size_t>(count));
        return result;
    }

#ifdef _WIN32
    namespace
    {
        std::uint64_t FileTimeTo100ns(const FILETIME& ft)
        {
            ULARGE_INTEGER v;
            v.LowPart = ft.dwLowDateTime;
            v.HighPart = ft.dwHighDateTime;
            return v.QuadPart;
        }

        std::uint64_t NowWall100ns()
        {
            FILETIME ft;
            GetSystemTimeAsFileTime(&ft); // 100-ns ticks; monotonic enough for a delta over ~1 min
            return FileTimeTo100ns(ft);
        }

        std::string NarrowExeName(const wchar_t* wide)
        {
            const int needed = WideCharToMultiByte(CP_UTF8, 0, wide, -1, nullptr, 0, nullptr, nullptr);
            if (needed <= 1)
                return std::string{};
            std::string out(static_cast<std::size_t>(needed - 1), '\0'); // drop the NUL
            WideCharToMultiByte(CP_UTF8, 0, wide, -1, out.data(), needed, nullptr, nullptr);
            return out;
        }
    } // namespace

    WindowsCpuSampler::WindowsCpuSampler()
    {
        SYSTEM_INFO info{};
        GetSystemInfo(&info);
        cores_ = info.dwNumberOfProcessors > 0 ? info.dwNumberOfProcessors : 1;
    }

    std::map<std::string, double> WindowsCpuSampler::Sample()
    {
        const std::uint64_t wall = NowWall100ns();

        // Snapshot: pid -> cumulative CPU 100ns, and pid -> exe name (for this tick).
        std::map<std::uint32_t, std::uint64_t> cur_cpu;
        std::map<std::uint32_t, std::string> cur_name;

        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot != INVALID_HANDLE_VALUE)
        {
            PROCESSENTRY32W entry{};
            entry.dwSize = sizeof(entry);
            if (Process32FirstW(snapshot, &entry))
            {
                do
                {
                    const std::uint32_t pid = entry.th32ProcessID;
                    if (pid == 0)
                        continue; // System Idle Process — not a real app

                    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
                    if (process == nullptr)
                        continue; // protected/elevated process we can't query — skip silently

                    FILETIME creation, exit, kernel, user;
                    if (GetProcessTimes(process, &creation, &exit, &kernel, &user))
                    {
                        cur_cpu[pid] = FileTimeTo100ns(kernel) + FileTimeTo100ns(user);
                        cur_name[pid] = NarrowExeName(entry.szExeFile);
                    }
                    CloseHandle(process);
                } while (Process32NextW(snapshot, &entry));
            }
            CloseHandle(snapshot);
        }

        std::map<std::string, double> by_name;

        const std::uint64_t wall_delta = wall > prev_wall_100ns_ ? wall - prev_wall_100ns_ : 0;
        if (have_baseline_ && wall_delta > 0)
        {
            const double denom = static_cast<double>(wall_delta) * static_cast<double>(cores_);
            for (const auto& [pid, cpu] : cur_cpu)
            {
                const auto prev = prev_cpu_100ns_.find(pid);
                if (prev == prev_cpu_100ns_.end() || cpu < prev->second)
                    continue; // new process this tick (or counter reset) — no delta to attribute

                const double percent = static_cast<double>(cpu - prev->second) / denom * 100.0;
                const auto name = cur_name.find(pid);
                if (name != cur_name.end() && !name->second.empty())
                    by_name[name->second] += percent;
            }
        }

        prev_cpu_100ns_ = std::move(cur_cpu);
        prev_wall_100ns_ = wall;
        have_baseline_ = true;
        return by_name; // empty on the first call (baseline only)
    }
#endif
} // namespace hsm::agent
