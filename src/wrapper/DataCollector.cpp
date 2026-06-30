#include "pch.h"

#include "DataCollector.h"
#include "DataCollectorImpl.h"

using namespace std;
using namespace hsm_wrapper;

// RedirectAssembly was a CLR assembly-binding shim for the managed HSMDataCollector.dll. The native
// backend has no managed dependency, so this is a no-op kept for source compatibility.
void hsm_wrapper::RedirectAssembly()
{
}

// ---- DataCollectorImplWrapper: forwards to the native DataCollectorImpl. The managed build wrapped
// every call in a try/catch that marshalled System::Exception into std::exception; native
// hsm::collector::Error already IS a std::exception, so the calls forward directly. --------------

DataCollectorImplWrapper::DataCollectorImplWrapper(const std::string& product_key, const std::string& address, int port, const std::string& module)
	: impl(new DataCollectorImpl(product_key, address, port, module))
{
}

void DataCollectorImplWrapper::Initialize(const std::string& config_path, bool write_debug)
{
	impl->Initialize(config_path, write_debug);
}

void DataCollectorImplWrapper::Stop()
{
	impl->Stop();
}

void DataCollectorImplWrapper::Start()
{
	impl->Start();
}

void DataCollectorImplWrapper::StartAsync()
{
	impl->StartAsync();
}

void DataCollectorImplWrapper::StopAsync()
{
	impl->StopAsync();
}

hsm::collector::Collector& DataCollectorImplWrapper::Native()
{
	return impl->Native();
}

void DataCollectorImplWrapper::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc)
{
	impl->InitializeSystemMonitoring(is_cpu, is_free_ram, is_time_in_gc);
}

void DataCollectorImplWrapper::InitializeCollectorMonitoring(bool is_alive, bool is_version, bool is_errors)
{
	impl->InitializeCollectorMonitoring(is_alive, is_version, is_errors);
}

void DataCollectorImplWrapper::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc)
{
	impl->InitializeProcessMonitoring(is_cpu, is_memory, is_threads, is_time_in_gc);
}

void DataCollectorImplWrapper::InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version)
{
	impl->InitializeOsMonitoring(is_last_update, is_last_restart, is_version);
}

void DataCollectorImplWrapper::InitializeOsLogsMonitoring(bool is_warnig, bool is_error)
{
	impl->InitializeOsLogsMonitoring(is_warnig, is_error);
}

void DataCollectorImplWrapper::AddServiceStateMonitoring(const std::string& service_name)
{
	impl->AddServiceStateMonitoring(service_name);
}

void DataCollectorImplWrapper::InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size)
{
	impl->InitializeQueueDiagnostic(is_overflow, is_process_time, is_values_count, is_content_size);
}

void DataCollectorImplWrapper::InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count)
{
	impl->InitializeNetworkMonitoring(is_failures_count, is_established_count, is_reset_count);
}

void DataCollectorImplWrapper::InitializeDiskMonitoring(const std::string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	impl->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void DataCollectorImplWrapper::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	impl->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void DataCollectorImplWrapper::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status, const std::string& description)
{
	impl->SendFileAsync(sensor_path, file_path, status, description);
}

void DataCollectorImplWrapper::InitializeProductVersion(const std::string& version)
{
	impl->InitializeProductVersion(version);
}

BoolSensor DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const std::string& description)
{
	return impl->CreateBoolSensor(path, description);
}

BoolSensor DataCollectorImplWrapper::CreateBoolSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl->CreateBoolSensor(path, options);
}

DoubleSensor DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	return impl->CreateDoubleSensor(path, description);
}

DoubleSensor DataCollectorImplWrapper::CreateDoubleSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl->CreateDoubleSensor(path, options);
}

IntSensor DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const std::string& description)
{
	return impl->CreateIntSensor(path, description);
}

IntSensor DataCollectorImplWrapper::CreateIntSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl->CreateIntSensor(path, options);
}

StringSensor DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const std::string& description)
{
	return impl->CreateStringSensor(path, description);
}

StringSensor DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const HSMInstantSensorOptions& options)
{
	return impl->CreateStringSensor(path, options);
}

BoolLastValueSensor DataCollectorImplWrapper::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
{
	return impl->CreateLastValueBoolSensor(path, default_value, description);
}

DoubleLastValueSensor DataCollectorImplWrapper::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
{
	return impl->CreateLastValueDoubleSensor(path, default_value, description);
}

StringLastValueSensor DataCollectorImplWrapper::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	return impl->CreateLastValueStringSensor(path, default_value, description);
}

IntLastValueSensor DataCollectorImplWrapper::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
{
	return impl->CreateLastValueIntSensor(path, default_value, description);
}

IntBarSensor DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	return impl->CreateIntBarSensor(path, timeout, small_period, description);
}

IntBarSensor DataCollectorImplWrapper::CreateIntBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	return impl->CreateIntBarSensor(path, options);
}

DoubleBarSensor DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	return impl->CreateDoubleBarSensor(path, timeout, small_period, precision, description);
}

DoubleBarSensor DataCollectorImplWrapper::CreateDoubleBarSensor(const std::string& path, const HSMBarSensorOptions& options)
{
	return impl->CreateDoubleBarSensor(path, options);
}

IntRateSensor DataCollectorImplWrapper::CreateIntRateSensor(const std::string& path, int period, const std::string& description) const
{
	return impl->CreateIntRateSensor(path, period, description);
}

IntRateSensor DataCollectorImplWrapper::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options) const
{
	return impl->CreateIntRateSensor(path, options);
}

DoubleRateSensor DataCollectorImplWrapper::CreateDoubleRateSensor(const std::string& path, int period, const std::string& description) const
{
	return impl->CreateDoubleRateSensor(path, period, description);
}

DoubleRateSensor DataCollectorImplWrapper::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options) const
{
	return impl->CreateDoubleRateSensor(path, options);
}

template<class T>
std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<T>> DataCollectorImplWrapper::CreateNoParamsFuncSensor(const std::string& path, const std::string& description,
	std::function<T()> function, const std::chrono::milliseconds& interval)
{
	return impl->CreateNoParamsFuncSensor(path, description, function, interval);
}

template<class T, class U>
shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> DataCollectorImplWrapper::CreateParamsFuncSensor(const std::string& path, const std::string& description,
	std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
{
	return impl->CreateParamsFuncSensor(path, description, function, interval);
}

// ---- DataCollectorProxy: pure forwarding to the wrapper (no CLR; reused unchanged from the managed
// build except that it now drives the native wrapper). -------------------------------------------

DataCollectorProxy::DataCollectorProxy(const std::string& product_key, const std::string& address, int port, const std::string& module)
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

hsm::collector::Collector& DataCollectorProxy::Native()
{
	return impl_wrapper->Native();
}

void DataCollectorProxy::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram, bool is_time_in_gc)
{
	impl_wrapper->InitializeSystemMonitoring(is_cpu, is_free_ram, is_time_in_gc);
}

void DataCollectorProxy::InitializeDiskMonitoring(const std::string& target, bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	impl_wrapper->InitializeDiskMonitoring(target, is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void DataCollectorProxy::InitializeAllDisksMonitoring(bool is_free_space, bool is_free_space_prediction, bool is_active_time, bool is_queue_lenght, bool is_average_speed)
{
	impl_wrapper->InitializeAllDisksMonitoring(is_free_space, is_free_space_prediction, is_active_time, is_queue_lenght, is_average_speed);
}

void DataCollectorProxy::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads, bool is_time_in_gc)
{
	impl_wrapper->InitializeProcessMonitoring(is_cpu, is_memory, is_threads, is_time_in_gc);
}

void DataCollectorProxy::InitializeOsMonitoring(bool is_last_update, bool is_last_restart, bool is_version)
{
	impl_wrapper->InitializeOsMonitoring(is_last_update, is_last_restart, is_version);
}

void DataCollectorProxy::InitializeOsLogsMonitoring(bool is_warnig, bool is_error)
{
	impl_wrapper->InitializeOsLogsMonitoring(is_warnig, is_error);
}

void DataCollectorProxy::AddServiceStateMonitoring(const std::string& service_name)
{
	impl_wrapper->AddServiceStateMonitoring(service_name);
}

void DataCollectorProxy::SendFileAsync(const std::string& sensor_path, const std::string& file_path, HSMSensorStatus status, const std::string& description)
{
	impl_wrapper->SendFileAsync(sensor_path, file_path, status, description);
}

void DataCollectorProxy::InitializeProductVersion(const std::string& version)
{
	impl_wrapper->InitializeProductVersion(version);
}

void DataCollectorProxy::InitializeQueueDiagnostic(bool is_overflow, bool is_process_time, bool is_values_count, bool is_content_size)
{
	impl_wrapper->InitializeQueueDiagnostic(is_overflow, is_process_time, is_values_count, is_content_size);
}

void DataCollectorProxy::InitializeCollectorMonitoring(bool is_alive, bool version, bool is_errors)
{
	impl_wrapper->InitializeCollectorMonitoring(is_alive, version, is_errors);
}

void DataCollectorProxy::InitializeNetworkMonitoring(bool is_failures_count, bool is_established_count, bool is_reset_count)
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

IntRateSensor DataCollectorProxy::CreateIntRateSensor(const std::string& path, int period, const std::string& description)
{
	return impl_wrapper->CreateIntRateSensor(path, period, description);
}

IntRateSensor DataCollectorProxy::CreateIntRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	return impl_wrapper->CreateIntRateSensor(path, options);
}

DoubleRateSensor DataCollectorProxy::CreateDoubleRateSensor(const std::string& path, int period, const std::string& description)
{
	return impl_wrapper->CreateDoubleRateSensor(path, period, description);
}

DoubleRateSensor DataCollectorProxy::CreateDoubleRateSensor(const std::string& path, const HSMRateSensorOptions& options)
{
	return impl_wrapper->CreateDoubleRateSensor(path, options);
}

#define InstantiateOneParamTemplates(X)                                                                                                                   \
	template HSMWRAPPER_API shared_ptr<HSMNoParamsFuncSensorImplWrapper<X>> DataCollectorImplWrapper::CreateNoParamsFuncSensor<X>(                        \
		const std::string& path, const std::string& description, function<X()> function, const std::chrono::milliseconds& interval);

#define InstantiateTwoParamTemplates(X, Y)                                                                                                                \
	template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<X, Y>> DataCollectorImplWrapper::CreateParamsFuncSensor<X, Y>(                      \
		const std::string& path, const std::string& description, function<X(const list<Y>&)> function, const std::chrono::milliseconds& interval);

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
