#pragma once

#include "HSMSensor.h"
#include "HSMBarSensor.h"
#include "HSMLastValueSensor.h"

namespace hsm_wrapper
{
	class DataCollectorImpl;

	class HSMWRAPPER_API DataCollectorProxy
	{
	public:
		DataCollectorProxy(const std::string& product_key, const std::string& address, int port);
		DataCollectorProxy() = delete;
		~DataCollectorProxy() = default;
		DataCollectorProxy(const DataCollectorProxy&) = default;
		DataCollectorProxy(DataCollectorProxy&&) = default;
		DataCollectorProxy& operator=(const DataCollectorProxy&) = default;
		DataCollectorProxy& operator=(DataCollectorProxy&&) = default;

		void Initialize(bool use_logging = true, const std::string& folder_path = "", const std::string& file_name_format = "");
		void Stop();
		void InitializeSystemMonitoring(bool is_cpu, bool is_free_ram);
		void InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads);
		void InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads);
		void MonitoringServiceAlive();

		BoolSensor CreateBoolSensor(const std::string& path, const std::string& description = "");
		IntSensor CreateIntSensor(const std::string& path, const std::string& description = "");
		DoubleSensor CreateDoubleSensor(const std::string& path, const std::string& description = "");
		StringSensor CreateStringSensor(const std::string& path, const std::string& description = "");
		BoolLastValueSensor CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = "");
		IntLastValueSensor CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = "");
		DoubleLastValueSensor CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = "");
		StringLastValueSensor CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = "");
		IntBarSensor CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = "");
		DoubleBarSensor CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = "");
	private:
		std::shared_ptr<DataCollectorImpl> impl;
	};
}