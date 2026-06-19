#pragma once

/// @file
/// @brief The Collector facade: lifecycle, callbacks, and every sensor factory over the C ABI.

#include "hsm_collector/alerts.hpp"
#include "hsm_collector/default_sensors.hpp"
#include "hsm_collector/detail/callbacks.hpp"
#include "hsm_collector/enums.hpp"
#include "hsm_collector/error.hpp"
#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/options.hpp"
#include "hsm_collector/sensors.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <future>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace hsm::collector
{
    /// Connection + pipeline configuration (mirrors hsm_collector_options_t / the managed
    /// CollectorOptions). Numeric `0` means "take the managed default"; pass an explicit value to
    /// override. `access_key` and `server_address` are required.
    struct CollectorOptions
    {
        std::string access_key;
        std::string server_address;
        int port = 443;
        std::string client_name;
        std::string module;
        std::string computer_name;

        int max_queue_size = 0;            ///< [20000]
        int max_values_in_package = 0;     ///< [1000]
        int package_collect_period_ms = 0; ///< [15000]
        int request_timeout_ms = 0;        ///< [30000]
        int max_sensors = 0;               ///< [100000]

        bool allow_untrusted_server_certificate = false;
        bool allow_plaintext_transport = false;

        std::int64_t exception_deduplicator_window_ms = 0; ///< [3600000]
        int max_deduplicated_messages = 0;                 ///< [1000]
    };

    /// Owning RAII handle to a native collector. Move-only. The destructor disposes and frees the
    /// underlying handle; std::function callbacks registered through this object are kept alive
    /// until after that happens.
    ///
    /// Threading: sensor `AddValue` is safe from any thread, but sensor/observer/sink REGISTRATION
    /// (the `Create*` factories, `AddLifecycleListener`, `SetLogger`, `SetMetricSourceFactory`) must
    /// be driven from a single thread — the internal callback storage is not synchronized. In
    /// particular, a callback running on the scheduler thread must not register from inside itself
    /// while another thread also registers. The natural pattern is to configure the collector fully
    /// before `Start()`.
    class Collector
    {
    public:
        explicit Collector(const CollectorOptions& options)
        {
            hsm_collector_options_t native{};
            native.access_key = options.access_key.c_str();
            native.server_address = options.server_address.c_str();
            native.port = options.port;
            native.client_name = options.client_name.empty() ? nullptr : options.client_name.c_str();
            native.module = options.module.empty() ? nullptr : options.module.c_str();
            native.computer_name = options.computer_name.empty() ? nullptr : options.computer_name.c_str();
            native.max_queue_size = options.max_queue_size;
            native.max_values_in_package = options.max_values_in_package;
            native.package_collect_period_ms = options.package_collect_period_ms;
            native.request_timeout_ms = options.request_timeout_ms;
            native.max_sensors = options.max_sensors;
            native.allow_untrusted_server_certificate = options.allow_untrusted_server_certificate;
            native.allow_plaintext_transport = options.allow_plaintext_transport;
            native.exception_deduplicator_window_ms = options.exception_deduplicator_window_ms;
            native.max_deduplicated_messages = options.max_deduplicated_messages;

            // handle_ is null on failure so there is no last-error string to pull; surface the
            // result-code name so the caller can tell INVALID_ARGUMENT (bad key/address/port) from
            // an internal error.
            const auto result = hsm_collector_create(&native, &handle_);
            if (result != HSM_RESULT_OK)
                throw Error(std::string{ "Failed to create collector. (" } + detail::ResultName(result) + ")");
        }

        Collector(const Collector&) = delete;
        Collector& operator=(const Collector&) = delete;

        Collector(Collector&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr)), lifecycle_listeners_(std::move(other.lifecycle_listeners_)), loggers_(std::move(other.loggers_)), metric_factories_(std::move(other.metric_factories_)), int_functions_(std::move(other.int_functions_)), int_values_functions_(std::move(other.int_values_functions_))
        {
        }

        Collector& operator=(Collector&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                handle_ = std::exchange(other.handle_, nullptr);
                lifecycle_listeners_ = std::move(other.lifecycle_listeners_);
                loggers_ = std::move(other.loggers_);
                metric_factories_ = std::move(other.metric_factories_);
                int_functions_ = std::move(other.int_functions_);
                int_values_functions_ = std::move(other.int_values_functions_);
            }

            return *this;
        }

        ~Collector()
        {
            Reset();
        }

        // ---- Lifecycle ----------------------------------------------------------------------

        void Start()
        {
            Check(hsm_collector_start(handle_), "Failed to start collector.");
        }

        void Stop()
        {
            Check(hsm_collector_stop(handle_), "Failed to stop collector.");
        }

        /// Start on a background thread. The returned future's task captures `this`, so the Collector
        /// must outlive the future and must NOT be moved or destroyed while the future is pending.
        /// Wait on (or discard) the returned future; note std::async's future blocks in its
        /// destructor until the task completes.
        std::future<void> StartAsync()
        {
            return std::async(std::launch::async, [this] { Start(); });
        }

        std::future<void> StopAsync()
        {
            return std::async(std::launch::async, [this] { Stop(); });
        }

        /// Probe server reachability (callable in any lifecycle state).
        void TestConnection()
        {
            Check(hsm_collector_test_connection(handle_), "TestConnection failed.");
        }

        /// Graceful, terminal, idempotent shutdown. Safe from any thread/state.
        void Dispose()
        {
            hsm_collector_dispose(handle_);
        }

        CollectorStatus Status() const
        {
            return static_cast<CollectorStatus>(hsm_collector_status(handle_));
        }

        // ---- Observers ----------------------------------------------------------------------

        /// Register a lifecycle observer. Fires after each transition; must not call a lifecycle
        /// method from within the callback.
        void AddLifecycleListener(LifecycleListener listener)
        {
            auto holder = std::make_unique<LifecycleListener>(std::move(listener));
            Check(
                hsm_collector_add_lifecycle_listener(handle_, &detail::hsm_collector_cpp_lifecycle_thunk, holder.get()),
                "Failed to add lifecycle listener.");
            lifecycle_listeners_.push_back(std::move(holder));
        }

        /// Install (or, with an empty std::function, clear) the log sink. Prefer to call this ONCE,
        /// before Start(). A replaced/cleared holder is retained (not freed) until the Collector is
        /// destroyed: the C side may still be invoking the previous logger on the scheduler thread,
        /// so freeing it here would be a use-after-free. There is no safe reclamation point, so
        /// repeatedly re-installing on a long-lived collector accumulates one holder per call.
        void SetLogger(Logger logger)
        {
            if (!logger)
            {
                Check(hsm_collector_set_logger(handle_, nullptr, nullptr), "Failed to clear logger.");
                return;
            }

            auto holder = std::make_unique<Logger>(std::move(logger));
            Check(hsm_collector_set_logger(handle_, &detail::hsm_collector_cpp_log_thunk, holder.get()), "Failed to set logger.");
            loggers_.push_back(std::move(holder));
        }

        /// Install (or, with an empty std::function, clear) the metric-source factory. As with
        /// SetLogger, call once before Start(): a replaced/cleared holder is retained until
        /// destruction (the factory may be in use on the scheduler thread), with no reclamation.
        void SetMetricSourceFactory(MetricSourceFactory factory)
        {
            if (!factory)
            {
                Check(
                    hsm_collector_set_metric_source_factory(handle_, nullptr, nullptr),
                    "Failed to clear metric-source factory.");
                return;
            }

            auto holder = std::make_unique<MetricSourceFactory>(std::move(factory));
            Check(
                hsm_collector_set_metric_source_factory(handle_, &detail::hsm_collector_cpp_metric_factory_thunk, holder.get()),
                "Failed to set metric-source factory.");
            metric_factories_.push_back(std::move(holder));
        }

        // ---- Instant sensors ----------------------------------------------------------------

        BoolSensor CreateBoolSensor(const std::string& path)
        {
            return BoolSensor(CreateSimple(&hsm_collector_create_bool_sensor, path, "Failed to create bool sensor."));
        }

        BoolSensor CreateBoolSensor(const std::string& path, const SensorOptions& options)
        {
            return BoolSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_BOOLEAN, options));
        }

        IntSensor CreateIntSensor(const std::string& path)
        {
            return IntSensor(CreateSimple(&hsm_collector_create_int_sensor, path, "Failed to create int sensor."));
        }

        IntSensor CreateIntSensor(const std::string& path, const SensorOptions& options)
        {
            return IntSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_INT, options));
        }

        DoubleSensor CreateDoubleSensor(const std::string& path)
        {
            return DoubleSensor(CreateSimple(&hsm_collector_create_double_sensor, path, "Failed to create double sensor."));
        }

        DoubleSensor CreateDoubleSensor(const std::string& path, const SensorOptions& options)
        {
            return DoubleSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_DOUBLE, options));
        }

        StringSensor CreateStringSensor(const std::string& path)
        {
            return StringSensor(CreateSimple(&hsm_collector_create_string_sensor, path, "Failed to create string sensor."));
        }

        StringSensor CreateStringSensor(const std::string& path, const SensorOptions& options)
        {
            return StringSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_STRING, options));
        }

        EnumSensor CreateEnumSensor(const std::string& path)
        {
            return EnumSensor(CreateSimple(&hsm_collector_create_enum_sensor, path, "Failed to create enum sensor."));
        }

        EnumSensor CreateEnumSensor(const std::string& path, const SensorOptions& options)
        {
            return EnumSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_ENUM, options));
        }

        /// Enum sensor with its EnumOptions table (key/value/color/description).
        EnumSensor CreateEnumSensor(
            const std::string& path,
            const std::string& description,
            const std::vector<EnumOption>& enum_options)
        {
            std::vector<hsm_enum_option_t> native;
            native.reserve(enum_options.size());
            for (const auto& option : enum_options)
            {
                hsm_enum_option_t entry{};
                entry.key = option.key;
                entry.value = option.value.c_str();
                entry.color = option.color;
                entry.description = option.description.c_str();
                native.push_back(entry);
            }

            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_enum_sensor_with_options(
                    handle_,
                    path.c_str(),
                    description.c_str(),
                    native.empty() ? nullptr : native.data(),
                    native.size(),
                    &sensor),
                "Failed to create enum sensor with options.");
            return EnumSensor(sensor);
        }

        TimeSpanSensor CreateTimeSpanSensor(const std::string& path)
        {
            return TimeSpanSensor(CreateSimple(&hsm_collector_create_timespan_sensor, path, "Failed to create timespan sensor."));
        }

        TimeSpanSensor CreateTimeSpanSensor(const std::string& path, const SensorOptions& options)
        {
            return TimeSpanSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_TIMESPAN, options));
        }

        VersionSensor CreateVersionSensor(const std::string& path)
        {
            return VersionSensor(CreateSimple(&hsm_collector_create_version_sensor, path, "Failed to create version sensor."));
        }

        VersionSensor CreateVersionSensor(const std::string& path, const SensorOptions& options)
        {
            return VersionSensor(CreateWithOptions(path, HSM_SENSOR_TYPE_VERSION, options));
        }

        // ---- Last-value sensors -------------------------------------------------------------

        BoolSensor CreateLastValueBoolSensor(const std::string& path, bool default_value)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_last_value_bool_sensor(handle_, path.c_str(), default_value, &sensor),
                "Failed to create last-value bool sensor.");
            return BoolSensor(sensor);
        }

        IntSensor CreateLastValueIntSensor(const std::string& path, std::int32_t default_value)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_last_value_int_sensor(handle_, path.c_str(), default_value, &sensor),
                "Failed to create last-value int sensor.");
            return IntSensor(sensor);
        }

        DoubleSensor CreateLastValueDoubleSensor(const std::string& path, double default_value)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_last_value_double_sensor(handle_, path.c_str(), default_value, &sensor),
                "Failed to create last-value double sensor.");
            return DoubleSensor(sensor);
        }

        StringSensor CreateLastValueStringSensor(const std::string& path, const std::string& default_value)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_last_value_string_sensor(handle_, path.c_str(), default_value.c_str(), &sensor),
                "Failed to create last-value string sensor.");
            return StringSensor(sensor);
        }

        // ---- Bar sensors --------------------------------------------------------------------

        IntBarSensor CreateIntBarSensor(const std::string& path, const BarOptions& options = {})
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_int_bar_sensor(
                    handle_,
                    path.c_str(),
                    static_cast<std::int64_t>(options.bar_period.count()),
                    static_cast<std::int64_t>(options.post_period.count()),
                    &sensor),
                "Failed to create int bar sensor.");
            return IntBarSensor(sensor);
        }

        DoubleBarSensor CreateDoubleBarSensor(const std::string& path, const BarOptions& options = {})
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_double_bar_sensor(
                    handle_,
                    path.c_str(),
                    static_cast<std::int64_t>(options.bar_period.count()),
                    static_cast<std::int64_t>(options.post_period.count()),
                    options.precision,
                    &sensor),
                "Failed to create double bar sensor.");
            return DoubleBarSensor(sensor);
        }

        // ---- Periodic sensors (rate / function) ---------------------------------------------

        RateSensor CreateRateSensor(const std::string& path, const RateOptions& options = {})
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_rate_sensor(
                    handle_,
                    path.c_str(),
                    static_cast<std::int64_t>(options.post_period.count()),
                    &sensor),
                "Failed to create rate sensor.");
            return RateSensor(sensor);
        }

        /// Pull int function sensor: `function` is invoked every period on the scheduler thread.
        FunctionSensor CreateFunctionSensor(
            const std::string& path,
            std::chrono::milliseconds post_period,
            IntFunction function)
        {
            auto holder = std::make_unique<IntFunction>(std::move(function));
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_function_int_sensor(
                    handle_,
                    path.c_str(),
                    static_cast<std::int64_t>(post_period.count()),
                    &detail::hsm_collector_cpp_int_function_thunk,
                    holder.get(),
                    &sensor),
                "Failed to create function sensor.");
            int_functions_.push_back(std::move(holder));
            return FunctionSensor(sensor);
        }

        /// Values-function sensor: AddValue buffers into a sliding window of `max_cache_size`; the
        /// callback receives a snapshot each period.
        ValuesFunctionSensor CreateValuesFunctionSensor(
            const std::string& path,
            std::chrono::milliseconds post_period,
            int max_cache_size,
            IntValuesFunction function)
        {
            auto holder = std::make_unique<IntValuesFunction>(std::move(function));
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_values_function_int_sensor(
                    handle_,
                    path.c_str(),
                    static_cast<std::int64_t>(post_period.count()),
                    max_cache_size,
                    &detail::hsm_collector_cpp_int_values_function_thunk,
                    holder.get(),
                    &sensor),
                "Failed to create values-function sensor.");
            int_values_functions_.push_back(std::move(holder));
            return ValuesFunctionSensor(sensor);
        }

        // ---- File / service-commands --------------------------------------------------------

        FileSensor CreateFileSensor(const std::string& path, const std::string& default_file_name, const std::string& extension)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_file_sensor(handle_, path.c_str(), default_file_name.c_str(), extension.c_str(), &sensor),
                "Failed to create file sensor.");
            return FileSensor(sensor);
        }

        ServiceCommandsSensor CreateServiceCommandsSensor()
        {
            hsm_sensor_t* sensor = nullptr;
            Check(hsm_collector_create_service_commands_sensor(handle_, &sensor), "Failed to create service-commands sensor.");
            return ServiceCommandsSensor(sensor);
        }

        // ---- Alerts -------------------------------------------------------------------------

        /// Begin building an alert. Attach the result to a sensor with Sensor::AttachAlert.
        AlertBuilder CreateAlert(AlertKind kind)
        {
            hsm_alert_t* alert = nullptr;
            Check(hsm_collector_create_alert(handle_, static_cast<hsm_alert_kind_t>(kind), &alert), "Failed to create alert.");
            return AlertBuilder(alert);
        }

        // ---- Default-sensor catalog ---------------------------------------------------------

        void AddDefaultSensor(DefaultSensor id)
        {
            Check(
                hsm_collector_add_default_sensor(handle_, static_cast<hsm_default_sensor_t>(id), nullptr, nullptr),
                "Failed to add default sensor.");
        }

        void AddDefaultSensor(DefaultSensor id, const DefaultSensorParams& params)
        {
            const auto native = params.ToNative();
            Check(
                hsm_collector_add_default_sensor(handle_, static_cast<hsm_default_sensor_t>(id), &native, nullptr),
                "Failed to add default sensor.");
        }

        void AddAllDefaultSensors(const std::string& product_version = "")
        {
            Check(
                hsm_collector_add_all_default_sensors(handle_, product_version.empty() ? nullptr : product_version.c_str()),
                "Failed to add all default sensors.");
        }

        void AddAllComputerSensors()
        {
            Check(hsm_collector_add_all_computer_sensors(handle_), "Failed to add computer sensors.");
        }

        void AddAllModuleSensors(const std::string& product_version = "")
        {
            Check(
                hsm_collector_add_all_module_sensors(handle_, product_version.empty() ? nullptr : product_version.c_str()),
                "Failed to add module sensors.");
        }

        void AddProcessMonitoringSensors()
        {
            Check(hsm_collector_add_process_monitoring_sensors(handle_), "Failed to add process sensors.");
        }

        void AddSystemMonitoringSensors()
        {
            Check(hsm_collector_add_system_monitoring_sensors(handle_), "Failed to add system sensors.");
        }

        void AddDiskMonitoringSensors()
        {
            Check(hsm_collector_add_disk_monitoring_sensors(handle_), "Failed to add disk sensors.");
        }

        void AddWindowsInfoMonitoringSensors()
        {
            Check(hsm_collector_add_windows_info_monitoring_sensors(handle_), "Failed to add windows-info sensors.");
        }

        void AddAllNetworkSensors()
        {
            Check(hsm_collector_add_all_network_sensors(handle_), "Failed to add network sensors.");
        }

        void AddCollectorMonitoringSensors()
        {
            Check(hsm_collector_add_collector_monitoring_sensors(handle_), "Failed to add collector-monitoring sensors.");
        }

        void AddAllQueueDiagnosticSensors()
        {
            Check(hsm_collector_add_all_queue_diagnostic_sensors(handle_), "Failed to add queue-diagnostic sensors.");
        }

        // ---- Introspection ------------------------------------------------------------------

        std::size_t SentCount() const
        {
            return hsm_collector_sent_count(handle_);
        }

        std::string SentJson(std::size_t index) const
        {
            const char* json = nullptr;
            Check(hsm_collector_get_sent_json(handle_, index, &json), "Sent payload not found.");
            return json == nullptr ? std::string{} : std::string{ json };
        }

        std::size_t RegistrationCount() const
        {
            return hsm_collector_registration_count(handle_);
        }

        std::string RegistrationJson(std::size_t index) const
        {
            const char* json = nullptr;
            Check(hsm_collector_get_registration_json(handle_, index, &json), "Registration payload not found.");
            return json == nullptr ? std::string{} : std::string{ json };
        }

        std::string LastError() const
        {
            const auto error = hsm_collector_last_error(handle_);
            return error == nullptr ? std::string{} : std::string{ error };
        }

        /// Borrowed C handle for interop with the raw ABI (still owned by this object).
        hsm_collector_t* handle() const
        {
            return handle_;
        }

    private:
        using SimpleCreateFn = hsm_result_t (*)(hsm_collector_t*, const char*, hsm_sensor_t**);

        hsm_sensor_t* CreateSimple(SimpleCreateFn create, const std::string& path, const char* context)
        {
            hsm_sensor_t* sensor = nullptr;
            Check(create(handle_, path.c_str(), &sensor), context);
            return sensor;
        }

        hsm_sensor_t* CreateWithOptions(const std::string& path, hsm_sensor_type_t type, const SensorOptions& options)
        {
            const auto native = options.ToNative();
            hsm_sensor_t* sensor = nullptr;
            Check(
                hsm_collector_create_sensor_with_options(handle_, path.c_str(), type, &native, &sensor),
                "Failed to create sensor with options.");
            return sensor;
        }

        void Check(hsm_result_t result, const char* context) const
        {
            if (result == HSM_RESULT_OK)
                return;

            const auto error = LastError();
            if (!error.empty())
                throw Error(error);

            throw Error(std::string{ context } + " (" + detail::ResultName(result) + ")");
        }

        void Reset()
        {
            // Destroy the C handle FIRST so the scheduler/lifecycle stop invoking any registered
            // thunk before the std::function storage below is freed (user_data must outlive the
            // collector). The trampoline members are destroyed after Reset() returns.
            hsm_collector_destroy(handle_);
            handle_ = nullptr;
        }

        hsm_collector_t* handle_ = nullptr;

        // std::function storage kept alive for the C ABI's `void* user_data`. unique_ptr keeps each
        // callable at a stable heap address across moves. All are append-only graveyards: a holder
        // handed to the C side is never freed until the Collector is destroyed (the scheduler thread
        // may still be invoking it), so SetLogger/SetMetricSourceFactory retain replaced holders too.
        std::vector<std::unique_ptr<LifecycleListener>> lifecycle_listeners_;
        std::vector<std::unique_ptr<Logger>> loggers_;
        std::vector<std::unique_ptr<MetricSourceFactory>> metric_factories_;
        std::vector<std::unique_ptr<IntFunction>> int_functions_;
        std::vector<std::unique_ptr<IntValuesFunction>> int_values_functions_;
    };
} // namespace hsm::collector
