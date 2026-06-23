// Windows metric-source factory (#1164). See the header for scope. Implemented with PDH (perf
// counters) and Win32 (free disk). Each source owns its PDH query / drive root and is freed by its
// dispose callback. A read failure returns HSM_METRIC_READ_ERROR so the collector recreates the
// source (managed recreate-on-error).

#include "hsm_windows_metric_sources.hpp"

#if defined(_WIN32)

#include <windows.h>

#include <pdh.h>
#include <pdhmsg.h>

#include <string>
#include <vector>

namespace hsm
{
    namespace platform
    {
        namespace
        {

            // A PDH source: one query holding one or more counters whose formatted DOUBLE values are summed and
            // scaled (e.g. bytes -> MB). Rate counters need two collects; the factory primes once, and the first
            // read may report PDH_CSTATUS_INVALID_DATA — reported as 0.0 (OK) rather than an error.
            struct PdhSource
            {
                PDH_HQUERY query = nullptr;
                std::vector<PDH_HCOUNTER> counters;
                double scale = 1.0;
            };

            struct DiskSource
            {
                std::wstring root; // e.g. "C:\\"
            };

            hsm_metric_read_t PdhRead(void* user_data, double* out_value)
            {
                auto* source = static_cast<PdhSource*>(user_data);
                if (source == nullptr || source->query == nullptr)
                    return HSM_METRIC_READ_ERROR;

                const PDH_STATUS collect = PdhCollectQueryData(source->query);
                if (collect != ERROR_SUCCESS)
                {
                    // Priming / transient at the query level: a just-bound rate or instance counter
                    // whose data isn't populated on this first/next collect (PDH_NO_DATA), or a
                    // momentarily-absent instance. Skip this tick and KEEP the primed query rather than
                    // returning ERROR — an ERROR makes the core dispose + recreate the source, throwing
                    // away the rate baseline and (under a persistent transient) churning every period.
                    // Only a genuine failure (e.g. an invalid handle) recreates.
                    if (collect == PDH_NO_DATA || collect == PDH_INVALID_DATA || collect == PDH_CSTATUS_NO_INSTANCE ||
                        collect == PDH_CSTATUS_NO_OBJECT || collect == PDH_CSTATUS_NO_COUNTER)
                        return HSM_METRIC_READ_NO_VALUE;
                    return HSM_METRIC_READ_ERROR;
                }

                double sum = 0.0;
                for (auto counter : source->counters)
                {
                    PDH_FMT_COUNTERVALUE value{};
                    const PDH_STATUS status = PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, nullptr, &value);
                    // Priming / transient statuses (a rate counter's first sample, a momentarily-absent
                    // instance, or a negative-denominator hiccup): report 0.0 OK so the sensor still
                    // posts this tick rather than skipping + recreating (which would stall a post for a
                    // whole period). Only a hard failure recreates the source.
                    if (status == PDH_CSTATUS_INVALID_DATA || status == PDH_CSTATUS_NO_INSTANCE ||
                        status == PDH_CALC_NEGATIVE_DENOMINATOR || status == PDH_CALC_NEGATIVE_VALUE ||
                        status == PDH_CALC_NEGATIVE_TIMEBASE)
                    {
                        *out_value = 0.0;
                        return HSM_METRIC_READ_OK;
                    }
                    if (status != ERROR_SUCCESS)
                        return HSM_METRIC_READ_ERROR;
                    sum += value.doubleValue;
                }

                *out_value = sum * source->scale;
                return HSM_METRIC_READ_OK;
            }

            void PdhDispose(void* user_data)
            {
                auto* source = static_cast<PdhSource*>(user_data);
                if (source == nullptr)
                    return;
                if (source->query != nullptr)
                    PdhCloseQuery(source->query);
                delete source;
            }

            hsm_metric_read_t DiskRead(void* user_data, double* out_value)
            {
                auto* source = static_cast<DiskSource*>(user_data);
                if (source == nullptr)
                    return HSM_METRIC_READ_ERROR;

                ULARGE_INTEGER free_bytes{};
                if (!GetDiskFreeSpaceExW(source->root.c_str(), &free_bytes, nullptr, nullptr))
                    return HSM_METRIC_READ_ERROR;

                *out_value = static_cast<double>(free_bytes.QuadPart) / (1024.0 * 1024.0); // -> MB
                return HSM_METRIC_READ_OK;
            }

            void DiskDispose(void* user_data)
            {
                delete static_cast<DiskSource*>(user_data);
            }

            bool Contains(const std::string& haystack, const char* needle)
            {
                return haystack.find(needle) != std::string::npos;
            }

            // The sensor NAME = the last path segment. The factory matches default-sensor labels against
            // the name, not the whole "<computer>/<module>/.../<name>" path, so a user-chosen computer or
            // module name that happens to contain a label fragment ("disk", "Total CPU", …) can't
            // mis-bind an unrelated PDH counter to that sensor.
            std::string SensorName(const std::string& path)
            {
                const auto slash = path.find_last_of('/');
                return slash == std::string::npos ? path : path.substr(slash + 1);
            }

            // Drive letter from a per-disk sensor name "... on <L> disk"; 0 if it can't be parsed.
            // The caller declines (registration-only) on 0 rather than guessing a volume — reporting a
            // different drive's space than the sensor name claims is the worst failure for monitoring.
            wchar_t DiskLetter(const std::string& path)
            {
                const auto pos = path.rfind(" disk");
                if (pos != std::string::npos && pos >= 1)
                {
                    const char c = path[pos - 1];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                        return static_cast<wchar_t>(c >= 'a' ? c - ('a' - 'A') : c);
                }
                return 0;
            }

            // The current process's executable base name without ".exe" — the PDH instance base.
            std::wstring CurrentProcessBaseName()
            {
                wchar_t buffer[MAX_PATH] = { 0 };
                const DWORD length = GetModuleFileNameW(nullptr, buffer, MAX_PATH);
                std::wstring name(buffer, length);
                const auto slash = name.find_last_of(L"\\/");
                if (slash != std::wstring::npos)
                    name = name.substr(slash + 1);
                const auto dot = name.rfind(L".exe");
                if (dot != std::wstring::npos)
                    name = name.substr(0, dot);
                return name.empty() ? L"_unknown" : name;
            }

            // Localized name of a well-known perf object/counter index (locale-independent input → the
            // host-language string PdhEnumObjectItemsW needs). Empty on failure.
            std::wstring PerfNameByIndex(DWORD index)
            {
                DWORD len = 0;
                const PDH_STATUS sized = PdhLookupPerfNameByIndexW(nullptr, index, nullptr, &len);
                if ((sized != PDH_MORE_DATA && sized != ERROR_SUCCESS) || len == 0)
                    return std::wstring();
                std::wstring name(len, L'\0');
                if (PdhLookupPerfNameByIndexW(nullptr, index, &name[0], &len) != ERROR_SUCCESS)
                    return std::wstring();
                name.resize(wcsnlen(name.c_str(), name.size()));
                return name;
            }

            // The PID owning a "\Process(<instance>)" set, via its ID Process counter (English path, so
            // locale-independent). 0 if it can't be read.
            DWORD ReadInstancePid(const std::wstring& instance)
            {
                PDH_HQUERY query = nullptr;
                if (PdhOpenQueryW(nullptr, 0, &query) != ERROR_SUCCESS)
                    return 0;

                PDH_HCOUNTER counter = nullptr;
                const std::wstring counter_path = L"\\Process(" + instance + L")\\ID Process";
                DWORD pid = 0;
                if (PdhAddEnglishCounterW(query, counter_path.c_str(), 0, &counter) == ERROR_SUCCESS &&
                    PdhCollectQueryData(query) == ERROR_SUCCESS)
                {
                    PDH_FMT_COUNTERVALUE value{};
                    if (PdhGetFormattedCounterValue(counter, PDH_FMT_LONG, nullptr, &value) == ERROR_SUCCESS)
                        pid = static_cast<DWORD>(value.longValue);
                }

                PdhCloseQuery(query);
                return pid;
            }

            // PDH instance name of the CURRENT process: "<base>" or "<base>#N" whose ID Process matches
            // our PID, so process counters bind to THIS process even when several share the executable
            // name. Falls back to the bare base name (the previous behavior) if the per-PID instance
            // can't be resolved — never worse than before. (PDH indices: Process=230.)
            std::wstring CurrentProcessInstance()
            {
                const std::wstring base = CurrentProcessBaseName();
                const DWORD pid = GetCurrentProcessId();

                const std::wstring process_object = PerfNameByIndex(230);
                if (process_object.empty())
                    return base;

                DWORD counter_len = 0;
                DWORD instance_len = 0;
                const PDH_STATUS sized = PdhEnumObjectItemsW(
                    nullptr, nullptr, process_object.c_str(), nullptr, &counter_len, nullptr, &instance_len,
                    PERF_DETAIL_WIZARD, 0);
                if ((sized != PDH_MORE_DATA && sized != ERROR_SUCCESS) || instance_len == 0)
                    return base;

                std::vector<wchar_t> counters(counter_len ? counter_len : 1, L'\0');
                std::vector<wchar_t> instances(instance_len, L'\0');
                if (PdhEnumObjectItemsW(
                        nullptr, nullptr, process_object.c_str(), counters.data(), &counter_len, instances.data(),
                        &instance_len, PERF_DETAIL_WIZARD, 0) != ERROR_SUCCESS)
                    return base;

                // instances is a MULTI_SZ; same-named processes appear as "base", "base#1", ...
                const std::wstring prefix = base + L"#";
                for (const wchar_t* it = instances.data(); *it != L'\0'; it += wcslen(it) + 1)
                {
                    const std::wstring instance(it);
                    if (instance != base && instance.compare(0, prefix.size(), prefix) != 0)
                        continue; // a different process name
                    if (ReadInstancePid(instance) == pid)
                        return instance;
                }
                return base;
            }

            // Build a primed single/dual-counter PDH source; returns nullptr if the query/counter can't open
            // (the path then stays registration-only). Primes once so rate counters have a baseline.
            PdhSource* MakePdhSource(const std::vector<std::wstring>& counter_paths, double scale)
            {
                PDH_HQUERY query = nullptr;
                if (PdhOpenQueryW(nullptr, 0, &query) != ERROR_SUCCESS)
                    return nullptr;

                auto* source = new PdhSource();
                source->query = query;
                source->scale = scale;
                for (const auto& path : counter_paths)
                {
                    PDH_HCOUNTER counter = nullptr;
                    // PdhAddEnglishCounterW takes the English counter path on ANY system locale (PdhAddCounterW
                    // would need localized names, e.g. on a Russian Windows), so these counters resolve regardless
                    // of the host language.
                    if (PdhAddEnglishCounterW(query, path.c_str(), 0, &counter) != ERROR_SUCCESS)
                    {
                        PdhDispose(source);
                        return nullptr;
                    }
                    source->counters.push_back(counter);
                }

                PdhCollectQueryData(query); // prime (ignore the first-sample status)
                return source;
            }

            bool Finish(
                void* source, hsm_metric_read_fn read, hsm_metric_dispose_fn dispose, hsm_metric_read_fn* out_read,
                hsm_metric_dispose_fn* out_dispose, void** out_source_user_data)
            {
                if (source == nullptr)
                    return false;
                *out_read = read;
                *out_dispose = dispose;
                *out_source_user_data = source;
                return true;
            }

        } // namespace

        int WindowsMetricSourceFactory(
            void* /*factory_user_data*/, const char* sensor_path, hsm_metric_read_fn* out_read,
            hsm_metric_dispose_fn* out_dispose, void** out_source_user_data)
        {
            if (sensor_path == nullptr || out_read == nullptr || out_dispose == nullptr || out_source_user_data == nullptr)
                return 0;

            const std::string name = SensorName(sensor_path);
            const auto pdh = [&](const std::vector<std::wstring>& paths, double scale) {
                return Finish(MakePdhSource(paths, scale), &PdhRead, &PdhDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            };

            // ---- System (unambiguous _Total / instance-less counters) ----
            if (Contains(name, "Total CPU"))
                return pdh({ L"\\Processor(_Total)\\% Processor Time" }, 1.0);
            if (Contains(name, "Free RAM"))
                return pdh({ L"\\Memory\\Available MBytes" }, 1.0);

            // ---- Disks (per drive letter; decline if the drive can't be parsed) ----
            if (Contains(name, "Free space on") && !Contains(name, "prediction"))
            {
                const wchar_t letter = DiskLetter(name);
                if (letter == 0)
                    return 0;
                auto* source = new DiskSource();
                source->root = std::wstring(1, letter) + L":\\";
                return Finish(source, &DiskRead, &DiskDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            }
            if (Contains(name, "Active time on"))
            {
                const wchar_t letter = DiskLetter(name);
                if (letter == 0)
                    return 0;
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, letter) + L":)\\% Disk Time" }, 1.0);
            }
            if (Contains(name, "Disk queue length on"))
            {
                const wchar_t letter = DiskLetter(name);
                if (letter == 0)
                    return 0;
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, letter) + L":)\\Avg. Disk Queue Length" }, 1.0);
            }
            if (Contains(name, "Average disk write speed on"))
            {
                const wchar_t letter = DiskLetter(name);
                if (letter == 0)
                    return 0;
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, letter) + L":)\\Disk Write Bytes/sec" }, 1.0 / (1024.0 * 1024.0));
            }

            // ---- Process (the current process, PID-resolved instance via CurrentProcessInstance) ----
            if (Contains(name, "Process CPU"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\% Processor Time" }, 1.0);
            if (Contains(name, "Process memory"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\Working Set" }, 1.0 / (1024.0 * 1024.0));
            if (Contains(name, "Process thread count"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\Thread Count" }, 1.0);

            // ---- Network (established gauge = TCPv4 + TCPv6; failure/reset deltas are a follow-up) ----
            if (Contains(name, "Connections Established"))
                return pdh({ L"\\TCPv4\\Connections Established", L"\\TCPv6\\Connections Established" }, 1.0);

            return 0; // not recognized -> stays registration-only
        }

    } // namespace platform
} // namespace hsm

#endif // _WIN32
