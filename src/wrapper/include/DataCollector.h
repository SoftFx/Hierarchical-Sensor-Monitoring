#pragma once

#include <string>
#include <type_traits>
#include <memory>
#include <list>
#include <chrono>
#include <functional>

#include "HSMSensor.h"
#include "HSMBarSensor.h"
#include "HSMRateSensor.h"
#include "HSMLastValueSensor.h"
#include "HSMParamsFuncSensor.h"
#include "HSMNoParamsFuncSensor.h"
#include "HSMSensorOptions.h"

namespace hsm_wrapper
{
	void HSMWRAPPER_API RedirectAssembly();

	class DataCollectorImpl;
	class DataCollectorProxy;

	class HSMWRAPPER_API DataCollectorImplWrapper
	{
	private:
		DataCollectorImplWrapper(const std::string& product_key, const std::string& address, int port, const std::string& module);

		void Initialize(const std::string& config_path = "", bool write_debug = false);
		void Start();
		void StartAsync();
		void Stop();
		void StopAsync();
		void InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc);
		void InitializeDiskMonitoring(const std::string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed);
		void InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed);
		void InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc);
		void InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version);
		void InitializeOsLogsMonitoring(bool is_warnig, bool is_error);
		void InitializeCollectorMonitoring(bool is_alive, bool is_version, bool is_errors);
		void InitializeProductVersion(const std::string& version);
		void InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count);
		void InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size);

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
		BoolLastValueSensor CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = "");
		IntLastValueSensor CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = "");
		DoubleLastValueSensor CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = "");
		StringLastValueSensor CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = "");
		IntBarSensor CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = "");
		IntBarSensor CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = "");
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		IntRateSensor CreateIntRateSensor(const std::string& path, int period, const std::string& description) const;
		IntRateSensor CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options) const;
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, int period, const std::string& description) const;
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options) const;

		template<class T>
		std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<T>> CreateNoParamsFuncSensor(const std::string& path, const std::string& description, std::function<T()> function, const std::chrono::milliseconds& interval);

		template<class T, class U> 
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T (const std::list<U>&)> function, const std::chrono::milliseconds& interval);

	private:
		std::shared_ptr<DataCollectorImpl> impl;

		friend DataCollectorProxy;
	};

	class HSMWRAPPER_API DataCollectorProxy
	{
	public:
		DataCollectorProxy(const std::string& product_key, const std::string& address, int port, const std::string& module);

		void Initialize(const std::string& config_path = "", bool write_debug = false);
		void Start();
		void StartAsync();
		void Stop();
		void StopAsync();
		void InitializeSystemMonitoring(bool is_cpu = true, bool is_free_ram = true, bool is_time_in_gc = true);
		void InitializeDiskMonitoring(const std::string& target, bool is_free_space = true, bool is_free_space_prediction = true, bool is_active_time = true, bool is_queue_lenght = true, bool is_average_speed = true);
		void InitializeAllDisksMonitoring(bool is_free_space = true, bool is_free_space_prediction = true, bool is_active_time = true, bool is_queue_lenght = true, bool is_average_speed = true);
		void InitializeProcessMonitoring(bool is_cpu = true, bool is_memory = true, bool is_threads = true, bool is_time_in_gc = true);
		void InitializeOsMonitoring(bool is_last_update = true, bool is_last_restart = true, bool is_version = true);
		void InitializeOsLogsMonitoring(bool is_warning = true, bool is_error = true);
		void InitializeCollectorMonitoring(bool is_alive = true, bool version = true, bool is_errors = true);
		void InitializeNetworkMonitoring(bool is_failures_count = true, bool is_established_count = true, bool is_reset_count = true);
		void InitializeProductVersion(const std::string& version); // version should be like a.b.c.d
		void InitializeQueueDiagnostic(bool is_overflow = true, bool is_process_time = true, bool is_values_count = true, bool is_content_size = true);
		
		void AddServiceStateMonitoring(const std::string& service_name);

		void SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status = HSMSensorStatus::Ok, const std::string& description = {});

		BoolSensor CreateBoolSensor(const std::string& path, const std::string& description = {});
		BoolSensor CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options);
		IntSensor CreateIntSensor(const std::string& path, const std::string& description = {});
		IntSensor CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options);
		DoubleSensor CreateDoubleSensor(const std::string& path, const std::string& description = {});
		DoubleSensor CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options);
		StringSensor CreateStringSensor(const std::string& path, const std::string& description = {});
		StringSensor CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options);
		BoolLastValueSensor CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = {});
		IntLastValueSensor CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = {});
		DoubleLastValueSensor CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = {});
		StringLastValueSensor CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = {});
		IntBarSensor CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = {});
		IntBarSensor CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = {});
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		IntRateSensor CreateIntRateSensor(const std::string& path, int period = 15000, const std::string& description = {});
		IntRateSensor CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options);
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, int period = 15000, const std::string& description = {});
		DoubleRateSensor CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options);

		template<class T>
		HSMNoParamsFuncSensor<T> CreateNoParamsFuncSensor(const std::string& path, const std::string& description, std::function<T()> func, const std::chrono::milliseconds& interval)
		{
			if constexpr (std::is_arithmetic_v<T> || std::is_same_v<T, std::string>)
			{
				return HSMNoParamsFuncSensor<T>{impl_wrapper->CreateNoParamsFuncSensor(path, description, func, interval)};
			}
			else
			{
				std::function<std::string()> wrapped_func = [func]()
				{
					return func().ToString();
				};
				return HSMNoParamsFuncSensor<T>{impl_wrapper->CreateNoParamsFuncSensor(path, description, wrapped_func, interval)};
			}
		}

		template<class T, class U>
		typename std::enable_if_t<std::is_arithmetic_v<T> && std::is_arithmetic_v<U>, HSMParamsFuncSensor<T, U>> 
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> func, const std::chrono::milliseconds& interval)
		{
			return HSMParamsFuncSensor<T, U>{ impl_wrapper->CreateParamsFuncSensor(path, description, func, interval) };
		}

		template<class T, class U>
		typename std::enable_if_t<!std::is_arithmetic_v<T> && std::is_arithmetic_v<U>, HSMParamsFuncSensor<T, U>> 
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> func, const std::chrono::milliseconds& interval)
		{
			std::function<std::string(const std::list<U>&)> wrapped_func;
			if constexpr (std::is_same_v<T, std::string>)
			{
				wrapped_func = [func](const std::list<U>& values)
				{
					return func(values);
				};
			}
			else
			{
				wrapped_func = [func](const std::list<U>& values)
				{
					return func(values).ToString();
				};
			}
			return HSMParamsFuncSensor<T, U>{ impl_wrapper->CreateParamsFuncSensor(path, description, wrapped_func, interval) };
		}

		template<class T, class U>
		typename std::enable_if_t<std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>, HSMParamsFuncSensor<T, U>> 
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> func, const std::chrono::milliseconds& interval)
		{
			std::function<T(const std::list<std::string>&)> wrapped_func = [func](const std::list<std::string>& values) -> T
			{
				std::list<U> converted_values;
				for (const std::string& value : values)
				{
					converted_values.push_back(move(U(value)));
				}
				return func(converted_values);
			};
			return HSMParamsFuncSensor<T, U>{ impl_wrapper->CreateParamsFuncSensor(path, description, wrapped_func, interval) };
		}

		template<class T, class U>
		typename std::enable_if_t<!std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>, HSMParamsFuncSensor<T, U>>
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> func, const std::chrono::milliseconds& interval)
		{
			std::function<std::string(const std::list<std::string>&)> wrapped_func;
			if constexpr (std::is_same_v<T, std::string>)
			{
				wrapped_func = [func](const std::list<std::string>& values)
				{
					std::list<U> converted_values;
					for (const std::string& value : values)
					{
						converted_values.push_back(move(U(value)));
					}
					return func(converted_values);
				};
			}
			else
			{
				wrapped_func = [func](const std::list<std::string>& values)
				{
					std::list<U> converted_values;
					for (const std::string& value : values)
					{
						converted_values.push_back(std::move(U(value)));
					}
					return func(converted_values).ToString();
				};
			}
			return HSMParamsFuncSensor<T, U>{ impl_wrapper->CreateParamsFuncSensor(path, description, wrapped_func, interval) };
		}		

	private:
		std::shared_ptr<DataCollectorImplWrapper> impl_wrapper;
	};

}


