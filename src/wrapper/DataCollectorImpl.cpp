#include "pch.h"

#include "DataCollectorImpl.h"

using namespace HSMDataCollector::Core;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMDataCollector::Logging;
using namespace HSMDataCollector::Options;
using namespace HSMDataCollector::Alerts;
using namespace HSMSensorDataObjects::SensorRequests;
using HSMSensorDataObjects::SensorStatus;
using System::String;
using System::Func;
using System::Collections::Generic::List;
using System::Threading::Tasks::Task;


using namespace std;
using namespace hsm_wrapper;


DataCollectorImpl::DataCollectorImpl(const std::string& product_key, const std::string& address, int port, const string& module)
{
	CollectorOptions^ options = gcnew CollectorOptions();
	options->AccessKey = gcnew String(product_key.c_str());
	options->ServerAddress = gcnew String(address.c_str());
	options->Port = port;
	options->Module = gcnew String(module.c_str());
	data_collector = gcnew DataCollector(options);
}

DataCollectorImpl::~DataCollectorImpl()
{
	if (!!start_task && !start_task->IsCompleted)
	{
		start_task->Wait();
	}
	if (!!stop_task && !stop_task->IsCompleted)
	{
		stop_task->Wait();
	}
}

void DataCollectorImpl::Initialize(const std::string& config_path, bool write_debug)
{
	LoggerOptions^ options = gcnew LoggerOptions();
	options->WriteDebug = write_debug;
	if (!config_path.empty())
		options->ConfigPath = gcnew String(config_path.c_str());

	data_collector->AddNLog(options);
}

void DataCollectorImpl::Start()
{
	data_collector->Start();
}

void DataCollectorImpl::StartAsync()
{
	start_task = Task::Run(gcnew System::Func<Task^>(data_collector.get(), &IDataCollector::Start));
}

void DataCollectorImpl::Stop()
{
	data_collector->Stop();
}

void DataCollectorImpl::StopAsync()
{
	stop_task = Task::Run(gcnew System::Func<Task^>(data_collector.get(), &IDataCollector::Stop));
}


void hsm_wrapper::DataCollectorImpl::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc)
{
	if (is_cpu) data_collector->Windows->AddTotalCpu();
	if (is_free_ram) data_collector->Windows->AddFreeRamMemory();
	if (is_time_in_gc) data_collector->Windows->AddGlobalTimeInGC();
}

void hsm_wrapper::DataCollectorImpl::InitializeDiskMonitoring(const std::string& target, bool is_free_space /*= true*/, bool is_free_space_prediction /*= true*/, bool is_active_time /*= true*/, bool is_queue_lenght /*= true*/, bool is_average_speed /*= true*/)
{
	auto disk_options = gcnew HSMDataCollector::Options::DiskSensorOptions();
	disk_options->TargetPath = gcnew System::String(target.c_str());

	if (is_free_space) data_collector->Windows->AddFreeDiskSpace(disk_options);
	if (is_free_space_prediction) data_collector->Windows->AddFreeDisksSpacePrediction(disk_options);

	auto disk_bar_options = gcnew HSMDataCollector::Options::DiskBarSensorOptions();
	disk_bar_options->TargetPath = gcnew System::String(target.c_str());

	if (is_active_time) data_collector->Windows->AddActiveDisksTime(disk_bar_options);
	if (is_queue_lenght) data_collector->Windows->AddDiskQueueLength(disk_bar_options);
	if (is_average_speed) data_collector->Windows->AddDiskAverageWriteSpeed(disk_bar_options);
}

void hsm_wrapper::DataCollectorImpl::InitializeAllDisksMonitoring(bool is_free_space /*= true*/, bool is_free_space_prediction /*= true*/, bool is_active_time /*= true*/, bool is_queue_lenght /*= true*/, bool is_average_speed /*= true*/)
{
	if (is_free_space) data_collector->Windows->AddFreeDisksSpace();
	if (is_free_space_prediction) data_collector->Windows->AddFreeDisksSpacePrediction();
	if (is_active_time) data_collector->Windows->AddActiveDisksTime();
	if (is_queue_lenght) data_collector->Windows->AddDisksQueueLength();
	if (is_average_speed) data_collector->Windows->AddDisksAverageWriteSpeed();
}

void hsm_wrapper::DataCollectorImpl::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc)
{
	if (is_cpu) data_collector->Windows->AddProcessCpu();
	if (is_memory) data_collector->Windows->AddProcessMemory();
	if (is_threads) data_collector->Windows->AddProcessThreadCount();
	if (is_time_in_gc) data_collector->Windows->AddProcessTimeInGC();
}

void hsm_wrapper::DataCollectorImpl::InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version)
{
 	if (is_last_update) data_collector->Windows->AddWindowsLastUpdate();
 	if (is_last_restart) data_collector->Windows->AddWindowsLastRestart();
	if (is_version) data_collector->Windows->AddWindowsVersion();
}

void hsm_wrapper::DataCollectorImpl::InitializeOsLogsMonitoring(bool is_warning, bool is_error)
{
	if (is_warning) data_collector->Windows->AddWarningWindowsLogs();
	if (is_error) data_collector->Windows->AddErrorWindowsLogs();
}


void hsm_wrapper::DataCollectorImpl::InitializeProductVersion(const std::string& version)
{
	VersionSensorOptions^ options = gcnew VersionSensorOptions();
	options->Version = gcnew System::Version(gcnew String(version.c_str()));
	data_collector->Windows->AddProductVersion(options);
}

void hsm_wrapper::DataCollectorImpl::InitializeCollectorMonitoring(bool is_alive, bool is_version, bool is_errors)
{
	if (is_alive) data_collector->Windows->AddCollectorAlive();
	if (is_version) data_collector->Windows->AddCollectorVersion();
	if (is_errors) data_collector->Windows->AddCollectorErrors();
}

void hsm_wrapper::DataCollectorImpl::AddServiceStateMonitoring(const std::string& service_name)
{
	data_collector->Windows->SubscribeToWindowsServiceStatus(gcnew String(service_name.c_str()));
}

void hsm_wrapper::DataCollectorImpl::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status /*= HSMSensorStatus::Ok*/, const std::string& description /*= {}*/)
{
	auto task = data_collector->SendFileAsync(gcnew String(sensor_path.c_str()), gcnew String(file_path.c_str()), SensorStatus{ status }, gcnew String(description.c_str()));
}

void DataCollectorImpl::InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size)
{
	if (is_overflow) data_collector->Windows->AddQueueOverflow();
	if (is_process_time) data_collector->Windows->AddQueuePackageProcessTime();
	if (is_values_count) data_collector->Windows->AddQueuePackageValuesCount();
	if (is_content_size) data_collector->Windows->AddQueuePackageContentSize();
}


void DataCollectorImpl::InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count)
{
	if (is_failures_count) data_collector->Windows->AddNetworkConnectionFailures();
	if (is_established_count) data_collector->Windows->AddNetworkConnectionsEstablished();
	if (is_reset_count) data_collector->Windows->AddNetworkConnectionsReset();
}

namespace {

	template <class T, class A>
	T^ ConvertAlert(const A& alert)
	{
		auto hsm_alert = alert.Impl()->GetAlert<T>();
		return hsm_alert;
	}

	template <class T, class A>
	List<T^>^ ConvertAlerts(const vector<A>& alerts)
	{
		auto hsm_alerts = gcnew List<T^>();

		for (auto& alert : alerts)
		{
			auto hsm_alert = ConvertAlert<T>(alert);
			hsm_alerts->Add(hsm_alert);
		}
		return hsm_alerts;
	}

	using namespace std::chrono;
	using tsticks = duration<long long, std::ratio<1, 10000000>>;

	template<class Dur>
	TimeSpan ToTimespan(const Dur& dur)
	{
		return TimeSpan::FromTicks(duration_cast<tsticks>(dur).count());
	}

	template<class Dur>
	TimeSpan ToTimespan(const optional<Dur>& dur)
	{
		return ToTimespan(dur.value());
	}

	void ConvertsBaseOptions(HSMDataCollector::Options::SensorOptions^ hsm_options, const hsm_wrapper::HSMBaseSensorOptions& options)
	{
		hsm_options->Description = gcnew String(options.description.c_str());
		if (options.keep_history)	hsm_options->KeepHistory = ToTimespan(options.keep_history);
		if (options.self_destroy)	hsm_options->SelfDestroy = ToTimespan(options.self_destroy);
		if (options.ttl)			hsm_options->TTL		 = ToTimespan(options.ttl);
		if (options.enable_for_grafana)		hsm_options->EnableForGrafana = options.enable_for_grafana.value();
		if (options.is_singleton_sensor)	hsm_options->IsSingletonSensor = options.is_singleton_sensor.value();
		if (options.aggregate_data)			hsm_options->AggregateData = options.aggregate_data.value();
		hsm_options->DefaultAlertsOptions = (DefaultAlertsOptions)options.default_alert_options;
	}

	HSMDataCollector::Options::InstantSensorOptions^ ConvertInstantOptions(const hsm_wrapper::HSMInstantSensorOptions& options)
	{
		auto hsm_options = gcnew InstantSensorOptions();

		ConvertsBaseOptions(hsm_options, options);
		
		hsm_options->Alerts = ConvertAlerts<InstantAlertTemplate>(options.alerts);

		return hsm_options;
	}

	HSMDataCollector::Options::BarSensorOptions^ ConvertBarOptions(const hsm_wrapper::HSMBarSensorOptions& options)
	{
		auto hsm_options = gcnew HSMDataCollector::Options::BarSensorOptions();

		ConvertsBaseOptions(hsm_options, options);

		hsm_options->BarPeriod = ToTimespan(options.bar_period);
		hsm_options->BarTickPeriod = ToTimespan(options.bar_tick_period);
		hsm_options->PostDataPeriod = ToTimespan(options.post_data_period);
		hsm_options->Precision = options.precision;
		hsm_options->Alerts = ConvertAlerts<BarAlertTemplate>(options.alerts);

		return hsm_options;
	}

	HSMDataCollector::Options::RateSensorOptions^ ConvertRateOptions(const hsm_wrapper::HSMRateSensorOptions& options)
	{
		auto hsm_options = gcnew HSMDataCollector::Options::RateSensorOptions();

		ConvertsBaseOptions(hsm_options, options);

		hsm_options->PostDataPeriod = ToTimespan(options.post_data_period);
		hsm_options->Alerts = ConvertAlerts<InstantAlertTemplate>(options.alerts);

		return hsm_options;
	}
}

HSMSensor<bool> DataCollectorImpl::CreateBoolSensor(const std::string& path, const std::string& description)
{
	auto bool_sensor = data_collector->CreateBoolSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<bool>{std::make_shared<HSMSensorImpl<bool>>(bool_sensor)};
}

HSMSensor<bool> DataCollectorImpl::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	auto bool_sensor = data_collector->CreateBoolSensor(gcnew String(path.c_str()), ConvertInstantOptions(options));
	return HSMSensor<bool>{std::make_shared<HSMSensorImpl<bool>>(bool_sensor)};
}

HSMSensor<int> DataCollectorImpl::CreateIntSensor(const std::string& path, const std::string& description)
{
	auto int_sensor = data_collector->CreateIntSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<int>{std::make_shared<HSMSensorImpl<int>>(int_sensor)};
}

HSMSensor<int> DataCollectorImpl::CreateIntSensor(const std::string& path, const hsm_wrapper::HSMInstantSensorOptions& options)
{
	auto int_sensor = data_collector->CreateIntSensor(gcnew String(path.c_str()), ConvertInstantOptions(options));
	return HSMSensor<int>{std::make_shared<HSMSensorImpl<int>>(int_sensor)};
}

HSMSensor<double> DataCollectorImpl::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	auto double_sensor = data_collector->CreateDoubleSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<double>{std::make_shared<HSMSensorImpl<double>>(double_sensor)};
}

HSMSensor<double> DataCollectorImpl::CreateDoubleSensor(const std::string& path, const hsm_wrapper::HSMInstantSensorOptions& options)
{
	auto double_sensor = data_collector->CreateDoubleSensor(gcnew String(path.c_str()), ConvertInstantOptions(options));
	return HSMSensor<double>{std::make_shared<HSMSensorImpl<double>>(double_sensor)};
}

HSMSensor<string> DataCollectorImpl::CreateStringSensor(const std::string& path, const std::string& description)
{
	auto string_sensor = data_collector->CreateStringSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<string>{std::make_shared<HSMSensorImpl<string>>(string_sensor)};
}

HSMSensor<string> DataCollectorImpl::CreateStringSensor(const std::string& path, const hsm_wrapper::HSMInstantSensorOptions& options)
{
	auto string_sensor = data_collector->CreateStringSensor(gcnew String(path.c_str()), ConvertInstantOptions(options));
	return HSMSensor<string>{std::make_shared<HSMSensorImpl<string>>(string_sensor)};
}

HSMLastValueSensor<bool> DataCollectorImpl::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
{
	auto int_default_sensor = data_collector->CreateLastValueBoolSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<bool>{std::make_shared<HSMLastValueSensorImpl<bool>>(int_default_sensor)};
}

HSMLastValueSensor<int> DataCollectorImpl::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
{
	auto int_default_sensor = data_collector->CreateLastValueIntSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<int>{std::make_shared<HSMLastValueSensorImpl<int>>(int_default_sensor)};
}

HSMLastValueSensor<double> DataCollectorImpl::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
{
	auto double_default_sensor = data_collector->CreateLastValueDoubleSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<double>{std::make_shared<HSMLastValueSensorImpl<double>>(double_default_sensor)};
}

HSMLastValueSensor<std::string> DataCollectorImpl::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	auto int_default_sensor = data_collector->CreateLastValueStringSensor(gcnew String(path.c_str()), gcnew String(default_value.c_str()), gcnew String(description.c_str()));
	return HSMLastValueSensor<string>{std::make_shared<HSMLastValueSensorImpl<std::string>>(int_default_sensor)};
}

HSMBarSensor<int> DataCollectorImpl::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	auto int_bar_sensor = data_collector->CreateIntBarSensor(gcnew String(path.c_str()), timeout, small_period, gcnew String(description.c_str()));
	return HSMBarSensor<int>{std::make_shared<HSMBarSensorImpl<int>>(int_bar_sensor)};
}

HSMBarSensor<int> DataCollectorImpl::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	auto int_bar_sensor = data_collector->CreateIntBarSensor(gcnew String(path.c_str()), ConvertBarOptions(options));
	return HSMBarSensor<int>{std::make_shared<HSMBarSensorImpl<int>>(int_bar_sensor)};
}

HSMBarSensor<double> DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	auto double_bar_sensor = data_collector->CreateDoubleBarSensor(gcnew String(path.c_str()), timeout, small_period, precision, gcnew String(description.c_str()));
	return HSMBarSensor<double>{std::make_shared<HSMBarSensorImpl<double>>(double_bar_sensor)};
}

HSMBarSensor<double> DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	auto double_bar_sensor = data_collector->CreateDoubleBarSensor(gcnew String(path.c_str()), ConvertBarOptions(options));
	return HSMBarSensor<double>{std::make_shared<HSMBarSensorImpl<double>>(double_bar_sensor)};
}

HSMRateSensor<int> DataCollectorImpl::CreateIntRateSensor(const std::string& path, int period /*= 60000*/, const std::string& description /*= ""*/)
{
	RateSensorOptions^ options = gcnew RateSensorOptions();
	options->PostDataPeriod = ToTimespan(chrono::milliseconds(period));
	auto int_rate_sensor = data_collector->CreateRateSensor(gcnew String(path.c_str()), options);
	return HSMRateSensor<int>{std::make_shared<HSMRateSensorImpl<int>>(int_rate_sensor)};
}

hsm_wrapper::HSMRateSensor<int> DataCollectorImpl::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	auto int_rate_sensor = data_collector->CreateRateSensor(gcnew String(path.c_str()), ConvertRateOptions(options));
	return HSMRateSensor<int>{std::make_shared<HSMRateSensorImpl<int>>(int_rate_sensor)};
}

HSMRateSensor<double> DataCollectorImpl::CreateDoubleRateSensor(const std::string& path, int period /*= 60000*/, const std::string& description /*= ""*/)
{
	RateSensorOptions^ options = gcnew RateSensorOptions();
	options->PostDataPeriod = ToTimespan(chrono::milliseconds(period));
	auto double_rate_sensor = data_collector->CreateRateSensor(gcnew String(path.c_str()), options);
	return HSMRateSensor<double>{std::make_shared<HSMRateSensorImpl<double>>(double_rate_sensor)};
}

hsm_wrapper::HSMRateSensor<double> DataCollectorImpl::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	auto double_rate_sensor = data_collector->CreateRateSensor(gcnew String(path.c_str()), ConvertRateOptions(options));
	return HSMRateSensor<double>{std::make_shared<HSMRateSensorImpl<double>>(double_rate_sensor)};
}


