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

                if (PdhCollectQueryData(source->query) != ERROR_SUCCESS)
                    return HSM_METRIC_READ_ERROR;

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

            // Drive letter from a per-disk sensor name "... on <L> disk" (default 'C').
            wchar_t DiskLetter(const std::string& path)
            {
                const auto pos = path.rfind(" disk");
                if (pos != std::string::npos && pos >= 1)
                {
                    const char c = path[pos - 1];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                        return static_cast<wchar_t>(c >= 'a' ? c - ('a' - 'A') : c);
                }
                return L'C';
            }

            // PDH instance name of the current process: the executable base name without ".exe".
            std::wstring CurrentProcessInstance()
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

            const std::string path = sensor_path;
            const auto pdh = [&](const std::vector<std::wstring>& paths, double scale) {
                return Finish(MakePdhSource(paths, scale), &PdhRead, &PdhDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            };

            // ---- System (unambiguous _Total / instance-less counters) ----
            if (Contains(path, "Total CPU"))
                return pdh({ L"\\Processor(_Total)\\% Processor Time" }, 1.0);
            if (Contains(path, "Free RAM"))
                return pdh({ L"\\Memory\\Available MBytes" }, 1.0);

            // ---- Disks (per drive letter) ----
            if (Contains(path, "Free space on") && !Contains(path, "prediction"))
            {
                auto* source = new DiskSource();
                source->root = std::wstring(1, DiskLetter(path)) + L":\\";
                return Finish(source, &DiskRead, &DiskDispose, out_read, out_dispose, out_source_user_data) ? 1 : 0;
            }
            if (Contains(path, "Active time on"))
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, DiskLetter(path)) + L":)\\% Disk Time" }, 1.0);
            if (Contains(path, "Disk queue length on"))
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, DiskLetter(path)) + L":)\\Avg. Disk Queue Length" }, 1.0);
            if (Contains(path, "Average disk write speed on"))
                return pdh({ L"\\LogicalDisk(" + std::wstring(1, DiskLetter(path)) + L":)\\Disk Write Bytes/sec" }, 1.0 / (1024.0 * 1024.0));

            // ---- Process (current process, best-effort instance; PID disambiguation is a follow-up) ----
            if (Contains(path, "Process CPU"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\% Processor Time" }, 1.0);
            if (Contains(path, "Process memory"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\Working Set" }, 1.0 / (1024.0 * 1024.0));
            if (Contains(path, "Process thread count"))
                return pdh({ L"\\Process(" + CurrentProcessInstance() + L")\\Thread Count" }, 1.0);

            // ---- Network (established gauge = TCPv4 + TCPv6; failure/reset deltas are a follow-up) ----
            if (Contains(path, "Connections Established"))
                return pdh({ L"\\TCPv4\\Connections Established", L"\\TCPv6\\Connections Established" }, 1.0);

            return 0; // not recognized -> stays registration-only
        }

    } // namespace platform
} // namespace hsm

#endif // _WIN32
