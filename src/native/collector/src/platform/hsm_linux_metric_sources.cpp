// Linux metric-source factory (#1414). See the header for scope. Every source reads the SAME OS
// truth the managed Unix sensor reads and runs the mirrored normalization (repo rule #10 — one
// sensor, one acquisition mechanism), so the two collectors cannot drift:
//
//   Total CPU            /proc/stat aggregate line, busy% by delta   <- UnixTotalCpu + ProcStatCpuUsage
//   Free RAM memory      /proc/meminfo MemAvailable, kB -> MB        <- UnixFreeRamMemory + ProcMeminfo
//   Free space on disk   statvfs("/") available bytes -> MB          <- UnixFreeDiskSpace + UnixDiskInfo
//   Free space ... prediction  statvfs("/") available kB -> TimeSpan <- UnixFreeDiskSpacePrediction
//   Process CPU          /proc/self/stat utime+stime delta           <- UnixProcessCpu
//   Process memory       /proc/self/stat rss pages -> MB             <- UnixProcessMemory (WorkingSet64)
//   Process thread count /proc/self/task entry count                 <- UnixProcessThreadCount
//
// The parsing/delta math lives in ../proc_metrics.hpp and the prediction math in
// ../disk_prediction.hpp so both are unit-testable without an OS read (the tcp_connection_stats.hpp
// precedent); this file is only the reads + the path binding.
//
// A read that FAILS reports HSM_METRIC_READ_SAMPLE_ERROR with the reason (#1426) instead of
// skipping silently: the collector logs it, posts it on `.module/Collector errors`, and — for a
// value sensor — posts one Error-status value, which is what the managed sensor does when its read
// throws. HSM_METRIC_READ_NO_VALUE is reserved for a tick that is legitimately empty (a delta
// source seeding its baseline, an interval too short to measure), which managed skips silently too.

#include "hsm_linux_metric_sources.hpp"

#if defined(__linux__)

#include "../disk_prediction.hpp"
#include "../proc_metrics.hpp"

#include <dirent.h>
#include <sys/statvfs.h>
#include <unistd.h>

#include <cerrno>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <exception>
#include <fstream>
#include <iterator>
#include <string>

namespace hsm
{
    namespace platform
    {
        namespace
        {
            using hsm::collector::DiskSpacePrediction;
            using hsm::collector::ParseMeminfoAvailableKb;
            using hsm::collector::ParseProcSelfStat;
            using hsm::collector::ParseProcStatCpuTimes;
            using hsm::collector::ProcessCpuUsage;
            using hsm::collector::ProcStatCpuUsage;

            constexpr const char* kProcStatPath = "/proc/stat";
            constexpr const char* kProcMeminfoPath = "/proc/meminfo";
            constexpr const char* kProcSelfStatPath = "/proc/self/stat";
            constexpr const char* kProcSelfTaskPath = "/proc/self/task";
            // UnixDiskInfo hard-codes the root mount; the Unix disk sensor has no per-drive segment.
            constexpr const char* kRootMount = "/";

            // FreeDiskSpacePredictionBase: DefaultSpaceCheckPeriodInSec sampling,
            // DiskSensorOptions.DefaultCalibrationRequests calibration posts.
            constexpr std::int64_t kSpaceCheckPeriodMs = 600000; // managed DefaultSpaceCheckPeriodInSec (#1445)
            constexpr std::int64_t kCalibrationRequests = 3;     // managed DiskSensorOptions.CalibrationRequests

            // Backing store for the strings a read hands back through hsm_metric_sample_t. The
            // collector copies them during the call and every read runs on one scheduler thread, so
            // per-thread storage is enough and keeps the stateless sources stateless.
            std::string& ScratchError()
            {
                static thread_local std::string buffer;
                return buffer;
            }

            std::string& ScratchComment()
            {
                static thread_local std::string buffer;
                return buffer;
            }

            hsm_metric_read_t Fail(hsm_metric_sample_t* sample, std::string reason)
            {
                ScratchError() = std::move(reason);
                sample->error = ScratchError().c_str();
                return HSM_METRIC_READ_SAMPLE_ERROR;
            }

            std::string ErrnoText(int code)
            {
                return std::to_string(code) + " (" + std::strerror(code) + ")";
            }

            // Whole-file read. /proc files are generated on read and must be consumed in one pass.
            // `error` receives the reason when the file cannot be opened or read.
            bool ReadWholeFile(const char* path, std::string& content, std::string& error)
            {
                errno = 0;
                std::ifstream file(path, std::ios::in | std::ios::binary);
                if (!file.is_open())
                {
                    error = std::string("cannot open ") + path + ": errno " + ErrnoText(errno);
                    return false;
                }

                content.assign(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
                if (file.bad())
                {
                    error = std::string("cannot read ") + path + ": errno " + ErrnoText(errno);
                    return false;
                }

                return true;
            }

            std::int64_t SteadyClockMilliseconds()
            {
                return std::chrono::duration_cast<std::chrono::milliseconds>(
                           std::chrono::steady_clock::now().time_since_epoch())
                    .count();
            }

            // Available space on the root filesystem in BYTES.
            //
            // DriveInfo.AvailableFreeSpace is computed by .NET's native PAL
            // (SystemNative_GetSpaceInfoForMountPoint, src/native/libs/System.Native/pal_mount.c)
            // as f_bsize * f_bavail — f_bsize, NOT f_frsize, in both its statfs and statvfs
            // branches. glibc's statvfs.f_bsize is statfs.f_bsize, so the same product is
            // reproduced here; using f_frsize would diverge on any filesystem whose fragment and
            // block sizes differ.
            bool ReadRootFreeBytes(std::uint64_t& bytes, std::string& error)
            {
                struct statvfs stats;
                if (statvfs(kRootMount, &stats) != 0)
                {
                    error = std::string("statvfs(\"") + kRootMount + "\") failed: errno " + ErrnoText(errno);
                    return false;
                }

                bytes = static_cast<std::uint64_t>(stats.f_bavail) * static_cast<std::uint64_t>(stats.f_bsize);
                return true;
            }

            // ---- Total CPU ------------------------------------------------------------------
            struct TotalCpuSource
            {
                // Deliberately NOT seeded at construction: the FIRST scheduled read seeds the
                // baseline and posts nothing, so the first value covers one full sample period.
                // UnixTotalCpu seeds in its constructor, but its first GetBarData() comes a full bar
                // tick later. Here the factory binds during Start and the first read fires within
                // milliseconds, so a construction seed would make the first sample a sub-millisecond
                // window in which a single jiffy reads as 0% or 100% — a spike the managed sensor never
                // produces and one that can trip the built-in EmaMean > 50 warning on every restart.
                // Seeding on the first read reproduces the managed timing: first value = usage over
                // [first tick, next tick].
                ProcStatCpuUsage usage{ std::string() };
            };

            hsm_metric_read_t TotalCpuRead(void* user_data, hsm_metric_sample_t* sample)
            {
                auto* source = static_cast<TotalCpuSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                std::string content;
                std::string error;
                if (!ReadWholeFile(kProcStatPath, content, error))
                    return Fail(sample, std::move(error));

                if (!ParseProcStatCpuTimes(content).has_value())
                    return Fail(sample, std::string("unexpected content in ") + kProcStatPath);

                const auto busy = source->usage.NextBusyPercent(content);
                if (!busy.has_value())
                    return HSM_METRIC_READ_NO_VALUE; // baseline seed / no elapsed jiffies — not a failure

                sample->double_value = *busy;
                return HSM_METRIC_READ_OK;
            }

            void TotalCpuDispose(void* user_data)
            {
                delete static_cast<TotalCpuSource*>(user_data);
            }

            // ---- Free RAM -------------------------------------------------------------------
            hsm_metric_read_t FreeRamRead(void* /*user_data*/, hsm_metric_sample_t* sample)
            {
                std::string content;
                std::string error;
                if (!ReadWholeFile(kProcMeminfoPath, content, error))
                    return Fail(sample, std::move(error));

                const auto available_kb = ParseMeminfoAvailableKb(content);
                if (!available_kb.has_value())
                    return Fail(sample, std::string("no usable memory fields in ") + kProcMeminfoPath);

                // UnixFreeRamMemory: availableKb / 1024.0 — a DOUBLE division (no truncation).
                sample->double_value = static_cast<double>(*available_kb) / 1024.0;
                return HSM_METRIC_READ_OK;
            }

            void NoOpDispose(void* /*user_data*/)
            {
            }

            // ---- Free disk space ------------------------------------------------------------
            hsm_metric_read_t FreeDiskRead(void* /*user_data*/, hsm_metric_sample_t* sample)
            {
                std::uint64_t available_bytes = 0;
                std::string error;
                if (!ReadRootFreeBytes(available_bytes, error))
                    return Fail(sample, std::move(error));

                // UnixDiskInfo reports (AvailableFreeSpace / 1024).KilobytesToMegabytes() — two
                // INTEGER divisions, so the value is whole megabytes with kB granularity lost.
                // Reproduced exactly here.
                const std::uint64_t available_mb = (available_bytes / 1024u) / 1024u;

                sample->double_value = static_cast<double>(available_mb);
                return HSM_METRIC_READ_OK;
            }

            // ---- Free disk space prediction --------------------------------------------------
            // UnixFreeDiskSpacePrediction: a 30 s sampling loop feeding the drain-speed EMA, and a
            // TimeSpan posted on the sensor's own post period. UnixDiskInfo.FreeSpace is
            // AvailableFreeSpace / 1024 — whole KILOBYTES — and the unit cancels in the division, so
            // the same kB figure is what this source samples and divides.
            struct DiskPredictionSource
            {
                DiskSpacePrediction prediction{ kCalibrationRequests };
                std::int64_t last_sample_ms = 0;
                bool has_last_sample = false;
            };

            bool ReadRootFreeKilobytes(double& kilobytes, std::string& error)
            {
                std::uint64_t bytes = 0;
                if (!ReadRootFreeBytes(bytes, error))
                    return false;

                kilobytes = static_cast<double>(bytes / 1024u);
                return true;
            }

            // The factory binds during Start, so seeding here is managed's StartAsync
            // (_lastAvailableSpace = FreeSpace) to the tick: without it the FIRST refresh would be
            // spent establishing the baseline and calibration would run one sampling period behind
            // managed, which now counts MEASUREMENTS (#1445). A failed read leaves the source
            // unseeded, exactly as managed leaves _hasBaseline false.
            void SeedDiskPrediction(DiskPredictionSource& source)
            {
                double free_kb = 0.0;
                std::string error;
                if (!ReadRootFreeKilobytes(free_kb, error))
                    return;

                source.prediction.Sample(free_kb, 0.0);
                source.last_sample_ms = SteadyClockMilliseconds();
                source.has_last_sample = true;
            }

            hsm_metric_read_t DiskPredictionRefresh(void* user_data, hsm_metric_sample_t* sample)
            {
                auto* source = static_cast<DiskPredictionSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                double free_kb = 0.0;
                std::string error;
                if (!ReadRootFreeKilobytes(free_kb, error))
                    return Fail(sample, std::move(error)); // managed TryReadFreeSpace -> HandleException

                const auto now_ms = SteadyClockMilliseconds();
                const double elapsed_seconds =
                    source->has_last_sample ? static_cast<double>(now_ms - source->last_sample_ms) / 1000.0 : 0.0;

                source->prediction.Sample(free_kb, elapsed_seconds);
                source->last_sample_ms = now_ms;
                source->has_last_sample = true;
                return HSM_METRIC_READ_NO_VALUE; // a sampling tick never posts
            }

            hsm_metric_read_t DiskPredictionRead(void* user_data, hsm_metric_sample_t* sample)
            {
                auto* source = static_cast<DiskPredictionSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                double free_kb = 0.0;
                std::string error;
                if (!ReadRootFreeKilobytes(free_kb, error))
                    return Fail(sample, std::move(error));

                const auto post = source->prediction.NextPost(free_kb);
                ScratchComment() = post.comment;

                sample->kind = HSM_METRIC_VALUE_TIMESPAN_MS;
                sample->timespan_ms = post.value_ms;
                sample->status = post.status;
                sample->comment = ScratchComment().c_str();
                return HSM_METRIC_READ_OK;
            }

            void DiskPredictionDispose(void* user_data)
            {
                delete static_cast<DiskPredictionSource*>(user_data);
            }

            // ---- Process CPU ----------------------------------------------------------------
            struct ProcessCpuSource
            {
                // Deliberately NOT primed at construction (same reasoning as TotalCpuSource): the
                // factory binds during Start and the first scheduled read follows after an arbitrary
                // few-to-tens of milliseconds, where a single quantized tick would read as tens or
                // hundreds of percent. The FIRST read seeds the baseline and posts nothing, so the
                // first posted value covers one full sample period — the managed timing, where
                // UnixProcessCpu's first GetBarData() comes a full bar tick after its constructor.
                // ProcessCpuUsage's one-tick minimum interval stays as a second line of defense.
                ProcessCpuSource()
                    : usage(static_cast<double>(::sysconf(_SC_CLK_TCK)))
                {
                }

                ProcessCpuUsage usage;
            };

            hsm_metric_read_t ProcessCpuRead(void* user_data, hsm_metric_sample_t* sample)
            {
                auto* source = static_cast<ProcessCpuSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                std::string content;
                std::string error;
                if (!ReadWholeFile(kProcSelfStatPath, content, error))
                    return Fail(sample, std::move(error));

                const auto stat = ParseProcSelfStat(content);
                if (!stat.has_value())
                    return Fail(sample, std::string("unexpected content in ") + kProcSelfStatPath);

                const auto percent =
                    source->usage.NextCpuPercent(stat->utime_ticks + stat->stime_ticks, SteadyClockMilliseconds());
                if (!percent.has_value())
                    return HSM_METRIC_READ_NO_VALUE; // baseline seed / sub-tick interval — not a failure

                sample->double_value = *percent;
                return HSM_METRIC_READ_OK;
            }

            void ProcessCpuDispose(void* user_data)
            {
                delete static_cast<ProcessCpuSource*>(user_data);
            }

            // ---- Process memory -------------------------------------------------------------
            hsm_metric_read_t ProcessMemoryRead(void* /*user_data*/, hsm_metric_sample_t* sample)
            {
                std::string content;
                std::string error;
                if (!ReadWholeFile(kProcSelfStatPath, content, error))
                    return Fail(sample, std::move(error));

                const auto stat = ParseProcSelfStat(content);
                if (!stat.has_value() || stat->rss_pages < 0)
                    return Fail(sample, std::string("unexpected content in ") + kProcSelfStatPath);

                const long page_size = ::sysconf(_SC_PAGESIZE);
                if (page_size <= 0)
                    return Fail(sample, "sysconf(_SC_PAGESIZE) returned no usable page size");

                // .NET's Process.WorkingSet64 on Linux is the /proc/<pid>/stat rss field in pages
                // times the page size; UnixProcessMemory then applies BytesToMegabytes, an INTEGER
                // division by 1 MiB. Same source, same truncation.
                const auto rss_bytes = static_cast<std::uint64_t>(stat->rss_pages) * static_cast<std::uint64_t>(page_size);
                sample->double_value = static_cast<double>(rss_bytes / (1024u * 1024u));
                return HSM_METRIC_READ_OK;
            }

            // ---- Process thread count -------------------------------------------------------
            hsm_metric_read_t ProcessThreadCountRead(void* /*user_data*/, hsm_metric_sample_t* sample)
            {
                // Process.Threads on Linux enumerates /proc/<pid>/task, so the thread count is the
                // number of task entries (excluding "." and "..").
                errno = 0;
                DIR* dir = ::opendir(kProcSelfTaskPath);
                if (dir == nullptr)
                    return Fail(sample, std::string("cannot open ") + kProcSelfTaskPath + ": errno " + ErrnoText(errno));

                // readdir returns NULL both at end-of-directory and on error; only errno tells them
                // apart. A failure part-way through must not post a partial count as a real one.
                double threads = 0.0;
                errno = 0;
                // Compared in place (no std::string): nothing between opendir and closedir may throw,
                // or the Guarded barrier would swallow the exception and leak the DIR handle.
                while (const dirent* entry = ::readdir(dir))
                {
                    if (std::strcmp(entry->d_name, ".") != 0 && std::strcmp(entry->d_name, "..") != 0)
                        threads += 1.0;
                    errno = 0;
                }
                const int read_error = errno;
                ::closedir(dir);

                if (read_error != 0)
                    return Fail(sample, std::string("cannot list ") + kProcSelfTaskPath + ": errno " + ErrnoText(read_error));
                if (threads <= 0.0)
                    return Fail(sample, std::string("no task entries under ") + kProcSelfTaskPath);

                sample->double_value = threads;
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

            // Publishes a source through the factory's out-param and returns the factory's "bound"
            // result (1). A stateless source passes nullptr as `source` — the seam only requires a
            // non-null read callback.
            int Finish(
                void* source, hsm_metric_read_sample_fn read, hsm_metric_dispose_fn dispose,
                hsm_metric_source_t* out_source, hsm_metric_read_sample_fn refresh = nullptr,
                std::int64_t refresh_period_ms = 0)
            {
                out_source->read_sample = read;
                out_source->refresh = refresh;
                out_source->refresh_period_ms = refresh_period_ms;
                out_source->dispose = dispose;
                out_source->user_data = source;
                return 1;
            }

            // Exception barrier for a read callback. The seam contract (hsm_collector.h) is that
            // callbacks never throw across the C boundary and the core does not catch around them, but
            // these readers allocate (whole-file reads, dirent names) and can raise std::bad_alloc.
            // Anything thrown is reported as a FAILED read rather than escaping into the scheduler
            // (root rule #6: callback exceptions must never crash the host), and since it is a sample
            // failure rather than a source fault the reader is kept — a transient allocation failure
            // must not throw away a delta source's baseline.
            template <hsm_metric_read_t (*Read)(void*, hsm_metric_sample_t*)>
            hsm_metric_read_t Guarded(void* user_data, hsm_metric_sample_t* sample) noexcept
            {
                try
                {
                    return Read(user_data, sample);
                }
                catch (const std::exception& ex)
                {
                    try
                    {
                        return Fail(sample, std::string("metric read threw: ") + ex.what());
                    }
                    catch (...)
                    {
                        sample->error = nullptr;
                        return HSM_METRIC_READ_SAMPLE_ERROR;
                    }
                }
                catch (...)
                {
                    sample->error = nullptr;
                    return HSM_METRIC_READ_SAMPLE_ERROR;
                }
            }

            int CreateSource(const std::string& name, hsm_metric_source_t* out_source);

        } // namespace

        int LinuxMetricSourceFactory(
            void* /*factory_user_data*/, const char* sensor_path, hsm_metric_source_t* out_source)
        {
            if (sensor_path == nullptr || out_source == nullptr)
                return 0;

            // Same barrier for the factory itself (it allocates the name and the stateful sources):
            // an exception declines the binding — the sensor stays registration-only — with clean
            // out-params, rather than unwinding through the C ABI.
            try
            {
                return CreateSource(SensorName(sensor_path), out_source);
            }
            catch (...)
            {
                out_source->read = nullptr;
                out_source->read_sample = nullptr;
                out_source->refresh = nullptr;
                out_source->refresh_period_ms = 0;
                out_source->dispose = nullptr;
                out_source->user_data = nullptr;
                return 0;
            }
        }

        namespace
        {
            int CreateSource(const std::string& name, hsm_metric_source_t* out_source)
            {
                // ---- System ----
                if (Contains(name, "Total CPU"))
                    return Finish(new TotalCpuSource(), &Guarded<TotalCpuRead>, &TotalCpuDispose, out_source);
                if (Contains(name, "Free RAM"))
                    return Finish(nullptr, &Guarded<FreeRamRead>, &NoOpDispose, out_source);

                // ---- Disk ----
                // EXACT match on the Unix rows' names, not a "Free space on" prefix. Both disk readers
                // always read the root mount, so a prefix match would also claim a letter-bearing
                // Windows row ("Free space on D disk" — still registerable here via add_default_sensor
                // with a disk_letter) and report the ROOT filesystem under a label that names another
                // volume, firing that sensor's alert off the wrong disk. The Windows factory refuses the
                // same thing for the same reason ("reporting a different drive's space than the sensor
                // name claims is the worst failure for monitoring"), so a letter-bearing row falls
                // through to registration-only here: empty-but-honest beats populated-but-wrong.
                if (name == "Free space on disk")
                    return Finish(nullptr, &Guarded<FreeDiskRead>, &NoOpDispose, out_source);
                if (name == "Free space on disk prediction")
                {
                    auto* prediction = new DiskPredictionSource();
                    SeedDiskPrediction(*prediction);
                    return Finish(
                        prediction, &Guarded<DiskPredictionRead>, &DiskPredictionDispose, out_source,
                        &Guarded<DiskPredictionRefresh>, kSpaceCheckPeriodMs);
                }

                // ---- Process (this process) ----
                if (Contains(name, "Process CPU"))
                    return Finish(new ProcessCpuSource(), &Guarded<ProcessCpuRead>, &ProcessCpuDispose, out_source);
                if (Contains(name, "Process memory"))
                    return Finish(nullptr, &Guarded<ProcessMemoryRead>, &NoOpDispose, out_source);
                if (Contains(name, "Process thread count"))
                    return Finish(nullptr, &Guarded<ProcessThreadCountRead>, &NoOpDispose, out_source);

                // "ThreadPool thread count" is a .NET runtime metric with no native equivalent, and the
                // Windows-only sensors (event logs, service status, network speed, top-CPU, OS info) are
                // explicitly out of scope here — all stay registration-only.
                return 0;
            }
        } // namespace

    } // namespace platform
} // namespace hsm

#endif // __linux__
