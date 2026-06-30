#pragma once

// Native backend for the DataCollector layer — holds an hsm::collector::Collector instead of a
// managed IDataCollector^. Non-template methods are defined in DataCollectorImpl.cpp; the function
// -sensor templates live here so DataCollector.cpp can instantiate them.

#include "DataCollector.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMRateSensorImpl.h"
#include "HSMLastValueSensorImpl.h"
#include "HSMNoParamsFuncSensorImpl.h"
#include "HSMParamsFuncSensorImpl.h"
#include "HSMSensorOptionsImpl.h"

#include "hsm_collector/hsm_collector.hpp"

#include <chrono>
#include <cstdint>
#include <functional>
#include <future>
#include <list>
#include <memory>
#include <mutex>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <vector>

namespace hsm_wrapper
{
	class DataCollectorImpl
	{
	public:
		DataCollectorImpl(const std::string& product_key, const std::string& address, int port, const std::string& module);

		void Initialize(const std::string& config_path = {}, bool write_debug = false);
		void Start();
		void StartAsync();
		void Stop();
		void StopAsync();

		// The underlying native collector — lets a consumer create new sensors directly against
		// hsm::collector while existing sensors still go through the wrapper (same collector, same
		// connection). The wrapper owns it; the reference is valid for the wrapper's lifetime.
		hsm::collector::Collector& Native() { return collector_; }
		void InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc);
		void InitializeDiskMonitoring(const std::string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed);
		void InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed);
		void InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc);
		void InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version);
		void InitializeOsLogsMonitoring(bool is_warning, bool is_error);
		void InitializeProductVersion(const std::string& version);
		void InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count);
		void InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size);
		void InitializeCollectorMonitoring(bool is_alive, bool is_version, bool is_errors);
		void AddServiceStateMonitoring(const std::string& service_name);

		void SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status = HSMSensorStatus::Ok, const std::string& description = {});

		BoolSensor CreateBoolSensor(const std::string& path, const std::string& description = "");
		BoolSensor CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options);
		IntSensor CreateIntSensor(const std::string& path, const std::string& description = "");
		IntSensor CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options);
		DoubleSensor CreateDoubleSensor(const std::string& path, const std::string& description = "");
		DoubleSensor CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options);
		StringSensor CreateStringSensor(const std::string& path, const std::string& description = "");
		StringSensor CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options);
		IntBarSensor CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = "");
		IntBarSensor CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = "");
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		IntRateSensor CreateIntRateSensor(const std::string& path, int period = 60000, const std::string& description = "");
		IntRateSensor CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options);
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, int period = 60000, const std::string& description = "");
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options);

		BoolLastValueSensor CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = "");
		IntLastValueSensor CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = "");
		DoubleLastValueSensor CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = "");
		StringLastValueSensor CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = "");

		// Sliding window for values-function sensors; large enough for the aggregator's per-window
		// accumulators (e.g. the obsolete reconnection summator). The managed buffer was unbounded.
		static constexpr int kFuncSensorCacheSize = 100000;

		template<class T>
		std::conditional_t<std::is_arithmetic_v<T>, std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<T>>, std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<std::string>>>
			CreateNoParamsFuncSensor(const std::string& path, const std::string& /*description*/, std::function<T()> function, const std::chrono::milliseconds& interval)
		{
			// The native function sensor is int-only. Restrict to T==int and fail loudly for any other
			// result type (double/bool/string) rather than silently truncating it to int32.
			if constexpr (std::is_same_v<T, int>)
			{
				auto native_sensor = collector_.CreateFunctionSensor(
					path, interval, [function]() -> std::int32_t { return function(); });
				auto impl = std::make_shared<HSMNoParamsFuncSensorImpl<T>>();
				impl->SetParamsFuncSensor(std::move(native_sensor), interval);
				auto wrapper = std::make_shared<HSMNoParamsFuncSensorImplWrapper<T>>(impl);
				wrapper->SetFunc(function);
				return wrapper;
			}
			else
			{
				throw hsm::collector::Error("Function sensors are int-only in the native collector; a non-int result type is not supported.");
			}
		}

		template<class T, class U>
		std::conditional_t<std::is_arithmetic_v<T>,
			std::conditional_t<std::is_arithmetic_v<U>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, std::string>>>,
			std::conditional_t<std::is_arithmetic_v<U>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<std::string, U>>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<std::string, std::string>>>>
			CreateParamsFuncSensor(const std::string& path, const std::string& /*description*/, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			// The native values-function sensor is int-result / int-element only. Restrict to <int,int>
			// and fail loudly for any other type rather than silently truncating elements or the result.
			if constexpr (std::is_same_v<T, int> && std::is_same_v<U, int>)
			{
				auto native_sensor = collector_.CreateValuesFunctionSensor(
					path, interval, kFuncSensorCacheSize,
					[function](const std::vector<std::int32_t>& values) -> std::int32_t {
						return function(std::list<U>(values.begin(), values.end()));
					});
				auto impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
				impl->SetParamsFuncSensor(std::move(native_sensor), interval);
				auto wrapper = std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(impl);
				wrapper->SetFunc(function);
				return wrapper;
			}
			else
			{
				throw hsm::collector::Error("Values-function sensors are int-result / int-element only in the native collector; other types are not supported.");
			}
		}

	private:
		hsm::collector::Collector collector_;
		// Hold the async lifecycle futures so the caller isn't blocked: a discarded std::future from
		// StartAsync/StopAsync blocks in its destructor until the task completes, which would make the
		// call effectively synchronous (managed StartAsync/StopAsync are fire-and-forget). Expected
		// usage is one-shot (a single StartAsync, a single StopAsync); calling StartAsync again while a
		// prior start is still pending blocks on the move-assignment until the prior task finishes.
		std::future<void> start_future_;
		std::future<void> stop_future_;
		std::mutex file_sensors_mutex_;
		std::unordered_map<std::string, hsm::collector::FileSensor> file_sensors_;
	};
}
