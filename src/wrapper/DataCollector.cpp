#include "pch.h"

#include "DataCollector.h"
#include "DataCollectorImpl.h"

#include "msclr/marshal_cppstd.h"

using namespace std;
using namespace hsm_wrapper;


DataCollectorImplWrapper::DataCollectorImplWrapper(const std::string& product_key, const std::string& address, int port, const std::string& module) 
try : impl( new DataCollectorImpl(product_key, address, port, module))
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

void DataCollectorImplWrapper::StartAsync()
{
	try
	{
		impl->StartAsync();
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::StopAsync()
{
	try
	{
		impl->StopAsync();
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc)
{
	try
	{
		impl->InitializeSystemMonitoring(is_cpu, is_free_ram, is_time_in_gc);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeCollectorMonitoring(bool is_alive, bool is_version, bool is_errors) 
{
	try
	{
		impl->InitializeCollectorMonitoring(is_alive, is_version, is_errors);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc)
{
	try
	{
		impl->InitializeProcessMonitoring(is_cpu, is_memory, is_threads, is_time_in_gc);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version)
{
	try
	{
		impl->InitializeOsMonitoring(is_last_update, is_last_restart, is_version);
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

void DataCollectorImplWrapper::InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size)
{
	try
	{
		impl->InitializeQueueDiagnostic(is_overflow, is_process_time, is_values_count, is_content_size);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count)
{
	try
	{
		impl->InitializeNetworkMonitoring(is_failures_count, is_established_count, is_reset_count);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeDiskMonitoring(const std::string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	try
	{
		impl->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void hsm_wrapper::DataCollectorImplWrapper::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	try
	{
		impl->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
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

BoolSensor DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const std::string& description)
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

BoolSensor DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
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

DoubleSensor DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const std::string& description)
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

DoubleSensor DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options)
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


IntSensor DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const std::string& description)
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

IntSensor DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options)
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


StringSensor DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const std::string& description)
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

StringSensor DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options)
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

BoolLastValueSensor DataCollectorImplWrapper::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
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

DoubleLastValueSensor DataCollectorImplWrapper::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
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

IntLastValueSensor DataCollectorImplWrapper::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
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

IntBarSensor DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
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


IntBarSensor DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
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


DoubleBarSensor DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
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

DoubleBarSensor DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
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

IntRateSensor DataCollectorImplWrapper::CreateIntRateSensor(const std::string& path, int period, const std::string& description) const
{
	try
	{
		return impl->CreateIntRateSensor(path, period, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}


IntRateSensor DataCollectorImplWrapper::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options) const
{
	try
	{
		return impl->CreateIntRateSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

DoubleRateSensor DataCollectorImplWrapper::CreateDoubleRateSensor(const std::string& path, int period, const std::string& description) const
{
	try
	{
		return impl->CreateDoubleRateSensor(path, period, description);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

DoubleRateSensor DataCollectorImplWrapper::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options) const
{
	try
	{
		return impl->CreateDoubleRateSensor(path, options);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

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


hsm_wrapper::DataCollectorProxy::DataCollectorProxy(const std::string& product_key, const std::string& address, int port, const std::string& module) 
	: impl_wrapper(new DataCollectorImplWrapper(product_key, address, port, module))
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

void DataCollectorProxy::StartAsync()
{
	impl_wrapper->StartAsync();
}

void DataCollectorProxy::StopAsync()
{
	impl_wrapper->StopAsync();
}

void hsm_wrapper::DataCollectorProxy::InitializeSystemMonitoring(bool is_cpu /*= true*/, bool is_free_ram /*= true*/, bool is_time_in_gc /*= true*/)
{
	impl_wrapper->InitializeSystemMonitoring(is_cpu, is_free_ram, is_time_in_gc);
}

void hsm_wrapper::DataCollectorProxy::InitializeDiskMonitoring(const std::string& target, bool is_free_space /*= true*/, bool is_free_space_prediction /*= true*/, bool is_active_time /*= true*/, bool is_queue_lenght /*= true*/, bool is_average_speed /*= true*/)
{
	impl_wrapper->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void hsm_wrapper::DataCollectorProxy::InitializeAllDisksMonitoring(bool is_free_space /*= true*/, bool is_free_space_prediction /*= true*/, bool is_active_time /*= true*/, bool is_queue_lenght /*= true*/, bool is_average_speed /*= true*/)
{
	impl_wrapper->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void hsm_wrapper::DataCollectorProxy::InitializeProcessMonitoring(bool is_cpu /*= true*/, bool is_memory /*= true*/, bool is_threads /*= true*/, bool is_time_in_gc /*= true*/)
{
	impl_wrapper->InitializeProcessMonitoring(is_cpu, is_memory, is_threads, is_time_in_gc);
}

void hsm_wrapper::DataCollectorProxy::InitializeOsMonitoring(bool is_last_update /*= true*/, bool is_last_restart /*= true*/, bool is_version /*= true*/)
{
	impl_wrapper->InitializeOsMonitoring(is_last_update, is_last_restart, is_version);
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

void DataCollectorProxy::InitializeQueueDiagnostic(bool is_overflow /*= true*/, bool is_process_time /*= true*/, bool is_values_count /*= true*/, bool is_content_size /*= true*/)
{
	impl_wrapper->InitializeQueueDiagnostic(is_overflow, is_process_time, is_values_count, is_content_size);
}

void hsm_wrapper::DataCollectorProxy::InitializeCollectorMonitoring(bool is_alive /*= true*/, bool version /*= true*/, bool is_errors /*= true*/)
{
	impl_wrapper->InitializeCollectorMonitoring(is_alive, version, is_errors);
}

void DataCollectorProxy::InitializeNetworkMonitoring(bool is_failures_count /*= true*/, bool is_established_count /*= true*/, bool is_reset_count /*= true*/)
{
	impl_wrapper->InitializeNetworkMonitoring(is_failures_count, is_established_count, is_reset_count);
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


IntBarSensor DataCollectorProxy::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	return impl_wrapper->CreateIntBarSensor(path, timeout, small_period, description);
}

IntBarSensor DataCollectorProxy::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
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


IntRateSensor DataCollectorProxy::CreateIntRateSensor(const std::string& path, int period /*= 15000*/, const std::string& description /*= {}*/)
{
	return impl_wrapper->CreateIntRateSensor(path, period, description);
}

IntRateSensor DataCollectorProxy::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	return impl_wrapper->CreateIntRateSensor(path, options);
}


DoubleRateSensor DataCollectorProxy::CreateDoubleRateSensor(const std::string& path, int period /*= 15000*/, const std::string& description /*= {}*/)
{
	return impl_wrapper->CreateDoubleRateSensor(path, period, description);
}

DoubleRateSensor DataCollectorProxy::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	return impl_wrapper->CreateDoubleRateSensor(path, options);
}


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

