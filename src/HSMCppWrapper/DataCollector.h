#pragma once

#include "HSMSensor.h"
#include "HSMBarSensor.h"
#include "HSMDefaultSensor.h"

namespace hsm_wrapper
{
	class DataCollectorImpl;

	class HSMWRAPPER_API DataCollectorProxy
	{
	public:
		DataCollectorProxy(const std::string& product_key, const std::string& address, int port);

		void Initialize(bool use_logging = true, const std::string& folder_path = "", const std::string& file_name_format = "");
		void Stop();
		void InitializeSystemMonitoring(bool is_cpu, bool is_free_ram);
		void InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads);
		void InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads);
		void MonitoringServiceAlive();

		HSMSensor<bool> CreateBoolSensor(const std::string& path);
		HSMSensor<double> CreateDoubleSensor(const std::string& path);
		HSMSensor<int> CreateIntSensor(const std::string& path);
		HSMSensor<const std::string&> CreateStringSensor(const std::string& path);
		HSMDefaultSensor<double> CreateDefaultValueSensorDouble(const std::string& path, double default_value);
		HSMDefaultSensor<int> CreateDefaultValueSensorInt(const std::string& path, int default_value);
		HSMBarSensor<double> CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2);
		HSMBarSensor<int> CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000);
	private:
		std::shared_ptr<DataCollectorImpl> impl;
	};
}