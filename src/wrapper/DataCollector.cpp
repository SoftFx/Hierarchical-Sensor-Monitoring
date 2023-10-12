#include "pch.h"

#include "DataCollector.h"
#include "DataCollectorImpl.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMLastValueSensorImpl.h"
#include "HSMBaseParamsFuncSensor.h"
#include "HSMParamsFuncSensorImpl.h"
#include "HSMSensorOptionsImpl.h"

#include "msclr/auto_gcroot.h"
#include "msclr/marshal_cppstd.h"


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

using namespace std;
using namespace hsm_wrapper;

hsm_wrapper::DataCollectorImpl::DataCollectorImpl(const std::string& product_key, const std::string& address, int port, const string& module)
{
	CollectorOptions^ options = gcnew CollectorOptions();
	options->AccessKey = gcnew String(product_key.c_str());
	options->ServerAddress = gcnew String(address.c_str());
	options->Port = port;
	options->Module = gcnew String(module.c_str());
	data_collector = gcnew DataCollector(options);
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


void DataCollectorImpl::Stop()
{
	data_collector->Stop();
}

void hsm_wrapper::DataCollectorImpl::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
{
	if (is_cpu) data_collector->Windows->AddTotalCpu();
	if (is_free_ram) data_collector->Windows->AddFreeRamMemory();
}

void DataCollectorImpl::InitializeDiskMonitoring(const string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	auto disk_options = gcnew HSMDataCollector::Options::DiskSensorOptions();
	disk_options->TargetPath = gcnew System::String(target.c_str());

	if (is_free_space) data_collector->Windows->AddFreeDiskSpace(disk_options);
	if (is_free_space_prediction) data_collector->Windows->AddFreeDisksSpacePrediction(disk_options);

	auto disk_bar_options = gcnew HSMDataCollector::Options::DiskBarSensorOptions();
	disk_bar_options->TargetPath = gcnew System::String(target.c_str());

	if (is_active_time) data_collector->Windows->AddActiveDisksTime(disk_bar_options);
	if (is_queue_lenght) data_collector->Windows->AddDisksQueueLength(disk_bar_options);
}

void hsm_wrapper::DataCollectorImpl::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	if (is_free_space) data_collector->Windows->AddFreeDisksSpace();
	if (is_free_space_prediction) data_collector->Windows->AddFreeDisksSpacePrediction();
	if (is_active_time) data_collector->Windows->AddActiveDisksTime();
	if (is_queue_lenght) data_collector->Windows->AddDisksQueueLength();
}

void hsm_wrapper::DataCollectorImpl::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
{
	if (is_cpu) data_collector->Windows->AddProcessCpu();
	if (is_memory) data_collector->Windows->AddProcessMemory();
	if (is_threads) data_collector->Windows->AddProcessThreadCount();
}

void hsm_wrapper::DataCollectorImpl::InitializeOsMonitoring(bool is_last_update, bool is_last_restart)
{
 	if (is_last_update) data_collector->Windows->AddWindowsLastUpdate();
 	if (is_last_restart) data_collector->Windows->AddWindowsLastRestart();
}

void hsm_wrapper::DataCollectorImpl::InitializeOsLogsMonitoring(bool is_warnig, bool is_error)
{
	if (is_warnig) data_collector->Windows->AddWarningWindowsLogs();
	if (is_error) data_collector->Windows->AddErrorWindowsLogs();
}


void hsm_wrapper::DataCollectorImpl::InitializeProductVersion(const std::string& version)
{
	VersionSensorOptions^ options = gcnew VersionSensorOptions();
	options->Version = gcnew System::Version(gcnew String(version.c_str()));
	data_collector->Windows->AddProductVersion(options);
}

void hsm_wrapper::DataCollectorImpl::InitializeCollectorMonitoring(bool is_alive, bool version)
{
	if (is_alive) data_collector->Windows->AddCollectorAlive();
	if (version) data_collector->Windows->AddCollectorVersion();
}

void hsm_wrapper::DataCollectorImpl::AddServiceStateMonitoring(const std::string& service_name)
{
	data_collector->Windows->SubscribeToWindowsServiceStatus(gcnew String(service_name.c_str()));
}

void hsm_wrapper::DataCollectorImpl::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status /*= HSMSensorStatus::Ok*/, const std::string& description /*= {}*/)
{
	auto task = data_collector->SendFileAsync(gcnew String(sensor_path.c_str()), gcnew String(file_path.c_str()), SensorStatus{ status }, gcnew String(description.c_str()));
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

	HSMDataCollector::Options::InstantSensorOptions^ ConvertInstantOptions(const hsm_wrapper::HSMInstantSensorOptions& options)
	{
		auto hsm_options = gcnew InstantSensorOptions();
		hsm_options->Description = gcnew String(options.description.c_str());
		hsm_options->Alerts = ConvertAlerts<InstantAlertTemplate>(options.alerts);
		return hsm_options;
	}

	HSMDataCollector::Options::BarSensorOptions^ ConvertBarOptions(const hsm_wrapper::HSMBarSensorOptions& options)
	{
		auto hsm_options = gcnew HSMDataCollector::Options::BarSensorOptions();
		hsm_options->Description = gcnew String(options.description.c_str());
		hsm_options->BarPeriod = TimeSpan::FromMilliseconds(options.bar_period);
		hsm_options->PostDataPeriod = TimeSpan::FromMilliseconds(options.post_data_period);
		hsm_options->Precision = options.precision;
		hsm_options->Alerts = ConvertAlerts<BarAlertTemplate>(options.alerts);
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

#ifdef ENABLE_OBSOLETE
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
#endif

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



DataCollectorImplWrapper::DataCollectorImplWrapper(const std::string& product_key, const std::string& address, int port, const std::string& module) 
try : impl( std::make_shared<DataCollectorImpl>(product_key, address, port, module))
{
}
catch (System::Exception^ ex)
{
	throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
}

void DataCollectorImplWrapper::Initialize(const std::string& config_path, bool write_debug)
{
	try
	{
		impl->Initialize(config_path, write_debug);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::Stop()
{
	try
	{
		impl->Stop();
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::Start()
{
	try
	{
		impl->Start();
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
{
	try
	{
		impl->InitializeSystemMonitoring(is_cpu, is_free_ram);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeCollectorMonitoring(bool is_alive, bool version, bool status) 
{
	try
	{
		impl->InitializeCollectorMonitoring(is_alive, version);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
{
	try
	{
		impl->InitializeProcessMonitoring(is_cpu, is_memory, is_threads);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeOsMonitoring(bool is_last_update, bool is_last_restart)
{
	try
	{
		impl->InitializeOsMonitoring(is_last_update, is_last_restart);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::InitializeOsLogsMonitoring(bool is_warnig, bool is_error)
{
	try
	{
		impl->InitializeOsLogsMonitoring(is_warnig, is_error);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::AddServiceStateMonitoring(const std::string& service_name)
{
	try
	{
		impl->AddServiceStateMonitoring(service_name);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::InitializeDiskMonitoring(const string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	try
	{
		impl->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	try
	{
		impl->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status /*= HSMSensorStatus::Ok*/, const std::string& description /*= {}*/)
{
	try
	{
		impl->SendFileAsync(sensor_path, file_path, status, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeProductVersion(const std::string& version) 
{
	try
	{
		impl->InitializeProductVersion(version);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMSensor<bool> DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const std::string& description)
{
	try
	{
		return impl->CreateBoolSensor(path, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

hsm_wrapper::HSMSensor<bool> DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	try
	{
		return impl->CreateBoolSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMSensor<double> DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	try
	{
		return impl->CreateDoubleSensor(path, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

hsm_wrapper::HSMSensor<double> DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	try
	{
		return impl->CreateDoubleSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}


HSMSensor<int> DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const std::string& description)
{
	try
	{
		return impl->CreateIntSensor(path, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

hsm_wrapper::HSMSensor<int> DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	try
	{
		return impl->CreateIntSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}


HSMSensor<std::string> DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const std::string& description)
{
	try
	{
		return impl->CreateStringSensor(path, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

hsm_wrapper::HSMSensor<std::string> DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	try
	{
		return impl->CreateStringSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

#ifdef ENABLE_OBSOLETE
HSMLastValueSensor<bool> DataCollectorImplWrapper::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
{
	try
	{
		return impl->CreateLastValueBoolSensor(path, default_value, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMLastValueSensor<double> DataCollectorImplWrapper::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
{
	try
	{
		return impl->CreateLastValueDoubleSensor(path, default_value, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

StringLastValueSensor DataCollectorImplWrapper::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	try
	{
		return impl->CreateLastValueStringSensor(path, default_value, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMLastValueSensor<int> DataCollectorImplWrapper::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
{
	try
	{
		return impl->CreateLastValueIntSensor(path, default_value, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}
#endif
HSMBarSensor<int> DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	try
	{
		return impl->CreateIntBarSensor(path, timeout, small_period, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}


hsm_wrapper::HSMBarSensor<int> DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	try
	{
		return impl->CreateIntBarSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}


HSMBarSensor<double> DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	try
	{
		return impl->CreateDoubleBarSensor(path, timeout, small_period, precision, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMBarSensor<double> DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	try
	{
		return impl->CreateDoubleBarSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}
#ifdef ENABLE_OBSOLETE
template<class T>
std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<T>> DataCollectorImplWrapper::CreateNoParamsFuncSensor(const std::string& path, const std::string& description, 
	std::function<T()> function, const std::chrono::milliseconds& interval)
{
	try
	{
		return impl->CreateNoParamsFuncSensor(path, description, function, interval);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

template<class T, class U>
shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> DataCollectorImplWrapper::CreateParamsFuncSensor(const std::string& path, const std::string& description, 
	std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
{
	try
	{
		return impl->CreateParamsFuncSensor(path, description, function, interval);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}
#endif

hsm_wrapper::DataCollectorProxy::DataCollectorProxy(const std::string& product_key, const std::string& address, int port, const std::string& module) 
	: impl_wrapper(std::make_shared<DataCollectorImplWrapper>(product_key, address, port, module))
{
}

void DataCollectorProxy::Initialize(const std::string& config_path, bool write_debug)
{
	impl_wrapper->Initialize(config_path, write_debug);
}

void DataCollectorProxy::Stop()
{
	impl_wrapper->Stop();
}

void DataCollectorProxy::Start()
{
	impl_wrapper->Start();
}

void hsm_wrapper::DataCollectorProxy::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
{
	impl_wrapper->InitializeSystemMonitoring(is_cpu, is_free_ram);
}

void DataCollectorProxy::InitializeDiskMonitoring(const string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	impl_wrapper->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght);
}

void hsm_wrapper::DataCollectorProxy::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght)
{
	impl_wrapper->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght);
}

void hsm_wrapper::DataCollectorProxy::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
{
	impl_wrapper->InitializeProcessMonitoring(is_cpu, is_memory, is_threads);
}

void hsm_wrapper::DataCollectorProxy::InitializeOsMonitoring(bool is_last_update, bool is_last_restart)
{
	impl_wrapper->InitializeOsMonitoring(is_last_update, is_last_restart);
}

void DataCollectorProxy::InitializeOsLogsMonitoring(bool is_warnig, bool is_error)
{
	impl_wrapper->InitializeOsLogsMonitoring(is_warnig, is_error);
}

void hsm_wrapper::DataCollectorProxy::AddServiceStateMonitoring(const std::string& service_name)
{
	impl_wrapper->AddServiceStateMonitoring(service_name);
}

void DataCollectorProxy::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status /*= HSMSensorStatus::Ok*/, const std::string& description /*= {}*/)
{
	impl_wrapper->SendFileAsync(sensor_path, file_path, status, description);
}

void hsm_wrapper::DataCollectorProxy::InitializeProductVersion(const std::string& version)
{
	impl_wrapper->InitializeProductVersion(version);
}

void hsm_wrapper::DataCollectorProxy::InitializeCollectorMonitoring(bool is_alive, bool version, bool status)
{
	impl_wrapper->InitializeCollectorMonitoring(is_alive, version, status);
}

BoolSensor DataCollectorProxy::CreateBoolSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateBoolSensor(path, description);
}

BoolSensor DataCollectorProxy::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl_wrapper->CreateBoolSensor(path, options);
}

IntSensor DataCollectorProxy::CreateIntSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateIntSensor(path, description);
}

IntSensor DataCollectorProxy::CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl_wrapper->CreateIntSensor(path, options);
}

DoubleSensor DataCollectorProxy::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateDoubleSensor(path, description);
}

DoubleSensor DataCollectorProxy::CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl_wrapper->CreateDoubleSensor(path, options);
}

StringSensor DataCollectorProxy::CreateStringSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateStringSensor(path, description);
}

StringSensor DataCollectorProxy::CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl_wrapper->CreateStringSensor(path, options);
}

#ifdef ENABLE_OBSOLETE
BoolLastValueSensor DataCollectorProxy::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
{
	return impl_wrapper->CreateLastValueBoolSensor(path, default_value, description);
}

IntLastValueSensor DataCollectorProxy::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
{
	return impl_wrapper->CreateLastValueIntSensor(path, default_value, description);
}

DoubleLastValueSensor DataCollectorProxy::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
{
	return impl_wrapper->CreateLastValueDoubleSensor(path, default_value, description);
}

StringLastValueSensor DataCollectorProxy::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	return impl_wrapper->CreateLastValueStringSensor(path, default_value, description);
}
#endif

IntBarSensor DataCollectorProxy::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	return impl_wrapper->CreateIntBarSensor(path, timeout, small_period, description);
}

hsm_wrapper::IntBarSensor DataCollectorProxy::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	return impl_wrapper->CreateIntBarSensor(path, options);
}


DoubleBarSensor DataCollectorProxy::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	return impl_wrapper->CreateDoubleBarSensor(path, timeout, small_period, precision, description);
}

DoubleBarSensor DataCollectorProxy::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	return impl_wrapper->CreateDoubleBarSensor(path, options);
}

#ifdef ENABLE_OBSOLETE

#define InstantiateOneParamTemplates(X)\
template shared_ptr<HSMNoParamsFuncSensorImplWrapper<X>> DataCollectorImpl::CreateNoParamsFuncSensor<X>\
(const std::string& path, const std::string& description, function<X()> function, const std::chrono::milliseconds& interval);\
template HSMWRAPPER_API shared_ptr<HSMNoParamsFuncSensorImplWrapper<X>> DataCollectorImplWrapper::CreateNoParamsFuncSensor<X>\
(const std::string& path, const std::string& description, function<X()> function, const std::chrono::milliseconds& interval);

#define InstantiateTwoParamTemplates(X, Y)\
template shared_ptr<HSMParamsFuncSensorImplWrapper<X, Y>> DataCollectorImpl::CreateParamsFuncSensor<X, Y>\
(const std::string& path, const std::string& description, function<X(const list<Y>&)> function, const std::chrono::milliseconds& interval);\
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<X, Y>> DataCollectorImplWrapper::CreateParamsFuncSensor<X, Y>\
(const std::string& path, const std::string& description, function<X(const list<Y>&)> function, const std::chrono::milliseconds& interval);

InstantiateOneParamTemplates(int)
InstantiateOneParamTemplates(double)
InstantiateOneParamTemplates(bool)
InstantiateOneParamTemplates(string)

InstantiateTwoParamTemplates(int, int)
InstantiateTwoParamTemplates(int, double)
InstantiateTwoParamTemplates(int, bool)
InstantiateTwoParamTemplates(int, string)

InstantiateTwoParamTemplates(double, int)
InstantiateTwoParamTemplates(double, double)
InstantiateTwoParamTemplates(double, bool)
InstantiateTwoParamTemplates(double, string)

InstantiateTwoParamTemplates(bool, int)
InstantiateTwoParamTemplates(bool, double)
InstantiateTwoParamTemplates(bool, bool)
InstantiateTwoParamTemplates(bool, string)

InstantiateTwoParamTemplates(string, int)
InstantiateTwoParamTemplates(string, double)
InstantiateTwoParamTemplates(string, bool)
InstantiateTwoParamTemplates(string, string)

#endif