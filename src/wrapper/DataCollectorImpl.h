#pragma once

#include "DataCollector.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMLastValueSensorImpl.h"
#include "HSMBaseParamsFuncSensor.h"
#include "HSMBaseNoParamsFuncSensor.h"
#include "HSMParamsFuncSensorImpl.h"
#include "HSMNoParamsFuncSensorImpl.h"
#include "HSMSensorOptionsImpl.h"

#include "msclr/auto_gcroot.h"
#include "msclr/marshal_cppstd.h"

namespace hsm_wrapper
{
	class DataCollectorImpl
	{
	public:
		DataCollectorImpl(const std::string& product_key, const std::string& address, int port, const std::string& module);
		~DataCollectorImpl();

		void Initialize(const std::string& config_path = {}, bool write_debug = false);
		void Start();
		void StartAsync();
		void Stop();
		void StopAsync();
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

		HSMSensor<bool> CreateBoolSensor(const std::string& path, const std::string& description = "");
		HSMSensor<bool> CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options);
		HSMSensor<int> CreateIntSensor(const std::string& path, const std::string& description = "");
		HSMSensor<int> CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options);
		HSMSensor<double> CreateDoubleSensor(const std::string& path, const std::string& description = "");
		HSMSensor<double> CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options);
		HSMSensor<std::string> CreateStringSensor(const std::string& path, const std::string& description = "");
		HSMSensor<std::string> CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options);
		HSMBarSensor<int> CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = "");
		HSMBarSensor<int> CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options);
		HSMBarSensor<double> CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = "");
		HSMBarSensor<double> CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options);

		HSMLastValueSensor<bool> CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = "");
		HSMLastValueSensor<int> CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = "");
		HSMLastValueSensor<double> CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = "");
		HSMLastValueSensor<std::string> CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = "");

		template<class T>
		typename std::conditional<std::is_arithmetic_v<T>, std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<T>>, std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<std::string>>>::type
			CreateNoParamsFuncSensor(const std::string& path, const std::string& description, std::function<T()> function, const std::chrono::milliseconds& interval)
		{
			using Type = typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type;
			auto no_params_func_sensor_impl = std::make_shared<HSMNoParamsFuncSensorImpl<T>>();
			no_params_func_sensor_impl->SetFunc(function);
			auto delegate_wrapper = no_params_func_sensor_impl->GetDelegateWrapper();
			auto no_params_func_sesnor = data_collector->CreateNoParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()),
				gcnew Func<Type>(delegate_wrapper, &NoParamsFuncDelegateWrapper<T>::Call), TimeSpan::FromMilliseconds(static_cast<double>(interval.count())));
			no_params_func_sensor_impl->SetParamsFuncSensor(no_params_func_sesnor);
			return std::make_shared<HSMNoParamsFuncSensorImplWrapper<T>>(no_params_func_sensor_impl);
		}

		template<class T, class U>
		typename std::conditional<std::is_arithmetic_v<T>,
			typename std::conditional<std::is_arithmetic_v<U>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, std::string>>>::type,
			typename std::conditional<std::is_arithmetic_v<U>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<std::string, U>>, std::shared_ptr<HSMParamsFuncSensorImplWrapper<std::string, std::string>>>::type>::type
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			using ResultType = typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type;
			using ElementType = typename std::conditional<std::is_arithmetic_v<U>, U, String^>::type;
			auto params_func_sensor_impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
			params_func_sensor_impl->SetFunc(function);
			auto delegate_wrapper = params_func_sensor_impl->GetDelegateWrapper();
			auto params_func_sensor = data_collector->CreateParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()), 
				gcnew Func<List<ElementType>^, ResultType>(delegate_wrapper, &ParamsFuncDelegateWrapper<T, U>::Call), TimeSpan::FromMilliseconds(static_cast<double>(interval.count())));
			params_func_sensor_impl->SetParamsFuncSensor(params_func_sensor);
			return std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(params_func_sensor_impl);
		}

	private:
		msclr::auto_gcroot<HSMDataCollector::Core::IDataCollector^> data_collector;
		msclr::auto_gcroot<System::Threading::Tasks::Task^> start_task;
		msclr::auto_gcroot<System::Threading::Tasks::Task^> stop_task;
	};

}