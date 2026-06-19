#pragma once

/// @file
/// @brief std::function -> C-callback trampolines and the metric-source seam interface.
///
/// Each C ABI callback takes a function pointer plus an opaque `void* user_data`. The wrapper
/// bridges a `std::function` by heap-allocating it (stable address) and passing its address as
/// `user_data` to a `static` thunk that casts back and invokes it. The C ABI requires `user_data`
/// to outlive the collector, so the owning Collector keeps these heap objects alive until AFTER it
/// calls `hsm_collector_destroy` (see collector.hpp). Storing them via `std::unique_ptr` keeps the
/// callable at a fixed address even when the Collector is moved.
///
/// The thunks are `extern "C"` to match the C-linkage callback typedefs. C language linkage ignores
/// the enclosing namespace, so they are prefixed `hsm_collector_cpp_*` to avoid colliding with
/// other unmangled C symbols a consumer might link.

#include "hsm_collector/enums.hpp"
#include "hsm_collector/hsm_collector.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <vector>

namespace hsm::collector
{
    /// Lifecycle observer: invoked AFTER each collector status transition. Must not call a
    /// lifecycle method (Start/Stop/Dispose) — that thread already holds the lifecycle lock.
    using LifecycleListener = std::function<void(CollectorStatus)>;

    /// Log sink: receives a level and the message text. Exceptions escaping it are swallowed.
    using Logger = std::function<void(LogLevel, const std::string&)>;

    /// Pull function for a function sensor: returns the value to post each scheduled tick.
    using IntFunction = std::function<std::int32_t()>;

    /// Pull function for a values-function sensor: receives a snapshot of the buffered window.
    using IntValuesFunction = std::function<std::int32_t(const std::vector<std::int32_t>&)>;

    /// The value source a default monitoring sensor reads each tick (IPerformanceCounter
    /// equivalent). Return a value, `std::nullopt` to skip this tick, or THROW to signal a read
    /// error (the collector disposes and recreates the source, matching the managed
    /// recreate-on-exception behavior).
    ///
    /// NOTE (#1099): the production metric-source seam is NOT yet wired into the scheduler — no
    /// default sensor reads it during normal operation today (the per-sensor scheduled-tick wiring
    /// + real OS readers are the #1099 live-value follow-up). Installing a factory before Start does
    /// not yet produce live values; the seam is exercised only via the test driver. See
    /// Collector::SetMetricSourceFactory.
    class IMetricSource
    {
    public:
        virtual ~IMetricSource() = default;
        virtual std::optional<double> Read() = 0;
    };

    /// Factory producing a metric source for a sensor's full registered path. Return `nullptr` to
    /// leave the sensor without a live source (it still registers). See IMetricSource for the
    /// #1099 "not yet wired" caveat.
    using MetricSourceFactory = std::function<std::unique_ptr<IMetricSource>(const std::string& sensor_path)>;

    namespace detail
    {
        extern "C" inline void hsm_collector_cpp_lifecycle_thunk(hsm_collector_status_t status, void* user_data)
        {
            try
            {
                (*static_cast<LifecycleListener*>(user_data))(static_cast<CollectorStatus>(status));
            }
            catch (...)
            {
            }
        }

        extern "C" inline void hsm_collector_cpp_log_thunk(hsm_log_level_t level, const char* message, void* user_data)
        {
            try
            {
                (*static_cast<Logger*>(user_data))(
                    static_cast<LogLevel>(level),
                    message == nullptr ? std::string{} : std::string{ message });
            }
            catch (...)
            {
            }
        }

        extern "C" inline std::int32_t hsm_collector_cpp_int_function_thunk(void* user_data)
        {
            try
            {
                return (*static_cast<IntFunction*>(user_data))();
            }
            catch (...)
            {
                return 0;
            }
        }

        extern "C" inline std::int32_t hsm_collector_cpp_int_values_function_thunk(const std::int32_t* values, std::int32_t count, void* user_data)
        {
            // Everything that can throw — including the snapshot allocation/copy (bad_alloc, or
            // length_error for a hostile count) — MUST stay inside the try: an exception escaping an
            // extern "C" function called on the scheduler thread is UB.
            try
            {
                std::vector<std::int32_t> snapshot;
                if (values != nullptr && count > 0)
                    snapshot.assign(values, values + count);

                return (*static_cast<IntValuesFunction*>(user_data))(snapshot);
            }
            catch (...)
            {
                return 0;
            }
        }

        // Metric-source thunks. The factory hands ownership of the created IMetricSource to the C
        // side as `source_user_data`; the dispose thunk deletes it (dispose is called exactly once,
        // including on the partial-failure path inside the core).
        extern "C" inline hsm_metric_read_t hsm_collector_cpp_metric_read_thunk(void* source_user_data, double* out_value)
        {
            try
            {
                const auto value = static_cast<IMetricSource*>(source_user_data)->Read();
                if (!value.has_value())
                    return HSM_METRIC_READ_NO_VALUE;

                *out_value = *value;
                return HSM_METRIC_READ_OK;
            }
            catch (...)
            {
                return HSM_METRIC_READ_ERROR;
            }
        }

        extern "C" inline void hsm_collector_cpp_metric_dispose_thunk(void* source_user_data)
        {
            // A throwing IMetricSource destructor is already a contract violation, but never let it
            // cross the extern "C" boundary.
            try
            {
                delete static_cast<IMetricSource*>(source_user_data);
            }
            catch (...)
            {
            }
        }

        extern "C" inline int hsm_collector_cpp_metric_factory_thunk(
            void* factory_user_data,
            const char* sensor_path,
            hsm_metric_read_fn* out_read,
            hsm_metric_dispose_fn* out_dispose,
            void** out_source_user_data)
        {
            std::unique_ptr<IMetricSource> source;
            try
            {
                source = (*static_cast<MetricSourceFactory*>(factory_user_data))(
                    sensor_path == nullptr ? std::string{} : std::string{ sensor_path });
            }
            catch (...)
            {
                return 0;
            }

            if (source == nullptr)
                return 0;

            *out_read = &hsm_collector_cpp_metric_read_thunk;
            *out_dispose = &hsm_collector_cpp_metric_dispose_thunk;
            *out_source_user_data = source.release();
            return 1;
        }
    } // namespace detail
} // namespace hsm::collector
