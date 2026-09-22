// Linux metric-source factory (#1414). See the header for scope. Every source reads the SAME OS
// truth the managed Unix sensor reads and runs the mirrored normalization (repo rule #10 — one
// sensor, one acquisition mechanism), so the two collectors cannot drift:
//
//   Total CPU            /proc/stat aggregate line, busy% by delta   <- UnixTotalCpu + ProcStatCpuUsage
//   Free RAM memory      /proc/meminfo MemAvailable, kB -> MB        <- UnixFreeRamMemory + ProcMeminfo
//   Free space on disk   statvfs("/") available bytes -> MB          <- UnixFreeDiskSpace + UnixDiskInfo
//   Process CPU          /proc/self/stat utime+stime delta           <- UnixProcessCpu
//   Process memory       /proc/self/stat rss pages -> MB             <- UnixProcessMemory (WorkingSet64)
//   Process thread count /proc/self/task entry count                 <- UnixProcessThreadCount
//
// The parsing/delta math lives in ../proc_metrics.hpp so it is unit-testable without an OS read
// (the tcp_connection_stats.hpp precedent); this file is only the reads + the path binding.
//
// "Free space on disk prediction" is declined here exactly as the Windows factory declines its
// prediction sensor: the metric seam is double-valued and the prediction sensor is a TimeSpan, so
// it stays registration-only on both platforms.

#include "hsm_linux_metric_sources.hpp"

#if defined(__linux__)

#include "../proc_metrics.hpp"

#include <dirent.h>
#include <sys/statvfs.h>
#include <unistd.h>

#include <chrono>
#include <fstream>
#include <iterator>
#include <string>

namespace hsm
{
    namespace platform
    {
        namespace
        {
            using hsm::collector::ParseMeminfoAvailableKb;
            using hsm::collector::ParseProcSelfStat;
            using hsm::collector::ProcessCpuUsage;
            using hsm::collector::ProcStatCpuUsage;

            constexpr const char* kProcStatPath = "/proc/stat";
            constexpr const char* kProcMeminfoPath = "/proc/meminfo";
            constexpr const char* kProcSelfStatPath = "/proc/self/stat";
            constexpr const char* kProcSelfTaskPath = "/proc/self/task";
            // UnixDiskInfo hard-codes the root mount; the Unix disk sensor has no per-drive segment.
            constexpr const char* kRootMount = "/";

            // Whole-file read. /proc files are generated on read and must be consumed in one pass;
            // a failure yields an empty string, which every parser turns into "no value" — mirroring
            // the managed sensors, whose IOException path returns null content rather than faulting.
            std::string ReadWholeFile(const char* path)
            {
                std::ifstream file(path, std::ios::in | std::ios::binary);
                if (!file.is_open())
                    return std::string();
                return std::string(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
            }

            std::int64_t SteadyClockMilliseconds()
            {
                return std::chrono::duration_cast<std::chrono::milliseconds>(
                           std::chrono::steady_clock::now().time_since_epoch())
                    .count();
            }

            // ---- Total CPU ------------------------------------------------------------------
            struct TotalCpuSource
            {
                // Seeded at construction so the first collected bar measures usage since the source
                // was created, not since boot (UnixTotalCpu's constructor does exactly this).
                ProcStatCpuUsage usage{ ReadWholeFile(kProcStatPath) };
            };

            hsm_metric_read_t TotalCpuRead(void* user_data, double* out_value)
            {
                auto* source = static_cast<TotalCpuSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                const auto busy = source->usage.NextBusyPercent(ReadWholeFile(kProcStatPath));
                if (!busy.has_value())
                    return HSM_METRIC_READ_NO_VALUE; // unreadable/unchanged sample -> skipped, not a fault

                *out_value = *busy;
                return HSM_METRIC_READ_OK;
            }

            void TotalCpuDispose(void* user_data)
            {
                delete static_cast<TotalCpuSource*>(user_data);
            }

            // ---- Free RAM -------------------------------------------------------------------
            hsm_metric_read_t FreeRamRead(void* /*user_data*/, double* out_value)
            {
                const auto available_kb = ParseMeminfoAvailableKb(ReadWholeFile(kProcMeminfoPath));
                if (!available_kb.has_value())
                    return HSM_METRIC_READ_NO_VALUE;

                // UnixFreeRamMemory: availableKb / 1024.0 — a DOUBLE division (no truncation).
                *out_value = static_cast<double>(*available_kb) / 1024.0;
                return HSM_METRIC_READ_OK;
            }

            void NoOpDispose(void* /*user_data*/)
            {
            }

            // ---- Free disk space ------------------------------------------------------------
            hsm_metric_read_t FreeDiskRead(void* /*user_data*/, double* out_value)
            {
                struct statvfs stats;
                if (statvfs(kRootMount, &stats) != 0)
                    return HSM_METRIC_READ_ERROR;

                // DriveInfo.AvailableFreeSpace on Linux is f_bavail * f_frsize (space available to an
                // unprivileged process), and UnixDiskInfo reports
                // (AvailableFreeSpace / 1024).KilobytesToMegabytes() — two INTEGER divisions, so the
                // value is whole megabytes with kB granularity lost. Reproduced exactly here.
                const auto available_bytes = static_cast<std::uint64_t>(stats.f_bavail) *
                                             static_cast<std::uint64_t>(stats.f_frsize);
                const std::uint64_t available_mb = (available_bytes / 1024u) / 1024u;

                *out_value = static_cast<double>(available_mb);
                return HSM_METRIC_READ_OK;
            }

            // ---- Process CPU ----------------------------------------------------------------
            struct ProcessCpuSource
            {
                ProcessCpuSource()
                    : usage(static_cast<double>(::sysconf(_SC_CLK_TCK)))
                {
                    Prime();
                }

                // Seeds the baseline at construction so the first scheduled read already yields a
                // percentage (UnixProcessCpu seeds _startCpuUsage/_startTime in its constructor).
                void Prime()
                {
                    const auto stat = ParseProcSelfStat(ReadWholeFile(kProcSelfStatPath));
                    if (stat.has_value())
                        (void)usage.NextCpuPercent(stat->utime_ticks + stat->stime_ticks, SteadyClockMilliseconds());
                }

                ProcessCpuUsage usage;
            };

            hsm_metric_read_t ProcessCpuRead(void* user_data, double* out_value)
            {
                auto* source = static_cast<ProcessCpuSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                const auto stat = ParseProcSelfStat(ReadWholeFile(kProcSelfStatPath));
                if (!stat.has_value())
                    return HSM_METRIC_READ_NO_VALUE;

                const auto percent =
                    source->usage.NextCpuPercent(stat->utime_ticks + stat->stime_ticks, SteadyClockMilliseconds());
                if (!percent.has_value())
                    return HSM_METRIC_READ_NO_VALUE;

                *out_value = *percent;
                return HSM_METRIC_READ_OK;
            }

            void ProcessCpuDispose(void* user_data)
            {
                delete static_cast<ProcessCpuSource*>(user_data);
            }

            // ---- Process memory -------------------------------------------------------------
            hsm_metric_read_t ProcessMemoryRead(void* /*user_data*/, double* out_value)
            {
                const auto stat = ParseProcSelfStat(ReadWholeFile(kProcSelfStatPath));
                if (!stat.has_value() || stat->rss_pages < 0)
                    return HSM_METRIC_READ_NO_VALUE;

                const long page_size = ::sysconf(_SC_PAGESIZE);
                if (page_size <= 0)
                    return HSM_METRIC_READ_NO_VALUE;

                // .NET's Process.WorkingSet64 on Linux is the /proc/<pid>/stat rss field in pages
                // times the page size; UnixProcessMemory then applies BytesToMegabytes, an INTEGER
                // division by 1 MiB. Same source, same truncation.
                const auto rss_bytes = static_cast<std::uint64_t>(stat->rss_pages) * static_cast<std::uint64_t>(page_size);
                *out_value = static_cast<double>(rss_bytes / (1024u * 1024u));
                return HSM_METRIC_READ_OK;
            }

            // ---- Process thread count -------------------------------------------------------
            hsm_metric_read_t ProcessThreadCountRead(void* /*user_data*/, double* out_value)
            {
                // Process.Threads on Linux enumerates /proc/<pid>/task, so the thread count is the
                // number of task entries (excluding "." and "..").
                DIR* dir = ::opendir(kProcSelfTaskPath);
                if (dir == nullptr)
                    return HSM_METRIC_READ_NO_VALUE;

                double threads = 0.0;
                while (const dirent* entry = ::readdir(dir))
                {
                    const std::string name(entry->d_name);
                    if (name == "." || name == "..")
                        continue;
                    threads += 1.0;
                }
                ::closedir(dir);

                if (threads <= 0.0)
                    return HSM_METRIC_READ_NO_VALUE;

                *out_value = threads;
                return HSM_METRIC_READ_OK;
            }

            bool Contains(const std::string& haystack, const char* needle)
            {
                return haystack.find(needle) != std::string::npos;
            }

            // The sensor NAME = the last path segment. Matching the name rather than the whole
            // "<computer>/<module>/.../<name>" path keeps a user-chosen computer/module name that
            // happens to contain a label fragment from mis-binding an unrelated source.
            std::string SensorName(const std::string& path)
            {
                const auto slash = path.find_last_of('/');
                return slash == std::string::npos ? path : path.substr(slash + 1);
            }

            bool Finish(
                void* source, hsm_metric_read_fn read, hsm_metric_dispose_fn dispose, hsm_metric_read_fn* out_read,
                hsm_metric_dispose_fn* out_dispose, void** out_source_user_data)
            {
                *out_read = read;
                *out_dispose = dispose;
                *out_source_user_data = source;
                return true;
            }

        } // namespace

        int LinuxMetricSourceFactory(
            void* /*factory_user_data*/, const char* sensor_path, hsm_metric_read_fn* out_read,
            hsm_metric_dispose_fn* out_dispose, void** out_source_user_data)
        {
            if (sensor_path == nullptr || out_read == nullptr || out_dispose == nullptr || out_source_user_data == nullptr)
                return 0;

            const std::string name = SensorName(sensor_path);

            // ---- System ----
            if (Contains(name, "Total CPU"))
                return Finish(new TotalCpuSource(), &TotalCpuRead, &TotalCpuDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            if (Contains(name, "Free RAM"))
                return Finish(nullptr, &FreeRamRead, &NoOpDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;

            // ---- Disk (the Unix sensor is the root mount; the prediction sensor is a TimeSpan) ----
            if (Contains(name, "Free space on") && !Contains(name, "prediction"))
                return Finish(nullptr, &FreeDiskRead, &NoOpDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;

            // ---- Process (this process) ----
            if (Contains(name, "Process CPU"))
                return Finish(new ProcessCpuSource(), &ProcessCpuRead, &ProcessCpuDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            if (Contains(name, "Process memory"))
                return Finish(nullptr, &ProcessMemoryRead, &NoOpDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            if (Contains(name, "Process thread count"))
                return Finish(nullptr, &ProcessThreadCountRead, &NoOpDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;

            // "ThreadPool thread count" is a .NET runtime metric with no native equivalent, and the
            // Windows-only sensors (event logs, service status, network speed, top-CPU, OS info) are
            // explicitly out of scope here — all stay registration-only.
            return 0;
        }

    } // namespace platform
} // namespace hsm

#endif // __linux__
