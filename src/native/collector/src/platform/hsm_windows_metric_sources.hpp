// Windows metric-source factory (#1164): backs the default-sensor metric seam with live PDH /
// Win32 readers. Matches a sensor's path tail to a counter (Total CPU, Free RAM, LogicalDisk
// gauges, free disk, process counters, TCP connections). Windows-only — the symbol exists only
// under _WIN32; the install entry point returns HSM_RESULT_INVALID_STATE elsewhere.
#pragma once

#if defined(_WIN32)

#include "hsm_collector/hsm_collector.h"

namespace hsm
{
    namespace platform
    {

        // hsm_metric_source_factory_fn: returns 1 and fills the read/dispose/user_data out-params when it
        // recognizes the path; returns 0 (no source) otherwise so the sensor stays registration-only.
        int WindowsMetricSourceFactory(
            void* factory_user_data,
            const char* sensor_path,
            hsm_metric_read_fn* out_read,
            hsm_metric_dispose_fn* out_dispose,
            void** out_source_user_data);

    } // namespace platform
} // namespace hsm

#endif // _WIN32
