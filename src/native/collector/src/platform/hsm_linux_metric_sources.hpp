// Linux metric-source factory (#1414): backs the default-sensor metric seam with live /proc and
// statvfs readers, mirroring the managed Unix sensors algorithm-for-algorithm (repo rule #10).
// Matches a sensor's path tail to a source (Total CPU, Free RAM, free disk, process CPU / memory /
// thread count). Linux-only — the symbol exists only under __linux__; the install entry point
// returns HSM_RESULT_INVALID_STATE elsewhere. Symmetric to hsm_windows_metric_sources.hpp.
#pragma once

#if defined(__linux__)

#include "hsm_collector/hsm_collector.h"

namespace hsm
{
    namespace platform
    {

        // hsm_metric_source_factory_fn: returns 1 and fills the read/dispose/user_data out-params when it
        // recognizes the path; returns 0 (no source) otherwise so the sensor stays registration-only.
        int LinuxMetricSourceFactory(
            void* factory_user_data,
            const char* sensor_path,
            hsm_metric_read_fn* out_read,
            hsm_metric_dispose_fn* out_dispose,
            void** out_source_user_data);

    } // namespace platform
} // namespace hsm

#endif // __linux__
