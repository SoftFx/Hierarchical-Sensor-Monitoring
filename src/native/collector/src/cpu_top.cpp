#include "cpu_top.hpp"

#include <algorithm>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <tlhelp32.h>
#endif

namespace hsm::collector
{
    std::vector<CpuUsage> SelectTopN(const std::map<std::string, CpuUsage>& by_name, int count, double min_percent)
    {
        std::vector<CpuUsage> result;
        if (count <= 0)
            return result;

        for (const auto& entry : by_name)
            if (entry.second.percent >= min_percent)
                result.push_back(entry.second);

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

        std::string NarrowUtf8(const wchar_t* wide)
        {
            const int needed = WideCharToMultiByte(CP_UTF8, 0, wide, -1, nullptr, 0, nullptr, nullptr);
            if (needed <= 1)
                return std::string{};
            std::string out(static_cast<std::size_t>(needed - 1), '\0');
            WideCharToMultiByte(CP_UTF8, 0, wide, -1, out.data(), needed, nullptr, nullptr);
            return out;
        }
    } // namespace

    WindowsCpuSampler::WindowsCpuSampler()
    {
        // Group-aware count: GetSystemInfo().dwNumberOfProcessors only sees the current processor
        // group (<= 64), which would over-report CPU% on large multi-group servers.
        const DWORD cores = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
        cores_ = cores > 0 ? cores : 1;
    }

    std::map<std::string, CpuUsage> WindowsCpuSampler::Sample()
    {
        // Monotonic elapsed time for the denominator — GetTickCount64 is immune to NTP/DST/manual
        // wall-clock adjustments (which could zero or inflate the interval).
        const std::uint64_t wall_ms = GetTickCount64();

        // This tick: pid -> {creation, cpu}, pid -> exe name, pid -> full path.
        std::map<std::uint32_t, PrevSample> cur;
        std::map<std::uint32_t, std::string> cur_name;
        std::map<std::string, std::string>   name_to_path; // first seen full path per exe name

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
                        cur[pid] = { FileTimeTo100ns(creation),
                                     FileTimeTo100ns(kernel) + FileTimeTo100ns(user) };

                        const std::string name = NarrowUtf8(entry.szExeFile);
                        cur_name[pid] = name;

                        // Full path via QueryFullProcessImageNameW — available under
                        // PROCESS_QUERY_LIMITED_INFORMATION (no SeDebugPrivilege needed).
                        if (!name.empty() && name_to_path.find(name) == name_to_path.end())
                        {
                            wchar_t path_buf[32768];
                            DWORD path_len = static_cast<DWORD>(std::size(path_buf));
                            if (QueryFullProcessImageNameW(process, 0, path_buf, &path_len))
                                name_to_path[name] = NarrowUtf8(path_buf);
                        }
                    }
                    CloseHandle(process);
                } while (Process32NextW(snapshot, &entry));
            }
            CloseHandle(snapshot);
        }

        std::map<std::string, CpuUsage> by_name;

        const std::uint64_t wall_delta_ms = wall_ms > prev_wall_ms_ ? wall_ms - prev_wall_ms_ : 0;
        if (have_baseline_ && wall_delta_ms > 0)
        {
            // Total CPU capacity over the interval, in 100ns units: ms * 10000 per core.
            const double denom = static_cast<double>(wall_delta_ms) * 10000.0 * static_cast<double>(cores_);
            for (const auto& [pid, sample] : cur)
            {
                const auto prev = prev_.find(pid);
                if (prev == prev_.end() || prev->second.creation_100ns != sample.creation_100ns || sample.cpu_100ns < prev->second.cpu_100ns)
                    continue;

                const double percent = static_cast<double>(sample.cpu_100ns - prev->second.cpu_100ns) / denom * 100.0;
                const auto name_it = cur_name.find(pid);
                if (name_it == cur_name.end() || name_it->second.empty())
                    continue;

                const std::string& name = name_it->second;
                by_name[name].name     = name;
                by_name[name].percent += percent;
                if (by_name[name].full_path.empty())
                {
                    const auto fp = name_to_path.find(name);
                    if (fp != name_to_path.end())
                        by_name[name].full_path = fp->second;
                }
            }
        }

        prev_ = std::move(cur);
        prev_wall_ms_ = wall_ms;
        have_baseline_ = true;
        return by_name; // empty on the first call (baseline only)
    }
#endif
} // namespace hsm::collector
