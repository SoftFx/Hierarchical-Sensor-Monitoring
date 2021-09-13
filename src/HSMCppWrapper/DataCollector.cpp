#include "pch.h"

#include "DataCollector.h"
#include "DataCollectorImpl.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMLastValueSensorImpl.h"
#include "HSMBaseParamsFuncSensor.h"
#include "HSMParamsFuncSensorImpl.h"

#include "msclr/auto_gcroot.h"
#include "msclr/marshal_cppstd.h"


using namespace HSMDataCollector::Core;
using namespace HSMDataCollector::PublicInterface;
using System::String;
using System::Func;
using System::Collections::Generic::List;

using namespace std;
using namespace hsm_wrapper;


DataCollectorImpl::DataCollectorImpl(const std::string& product_key, const std::string& address, int port)
{
	data_collector = gcnew DataCollector(gcnew String(product_key.c_str()), gcnew String(address.c_str()), port);
}

void DataCollectorImpl::Initialize(bool use_logging, const std::string& folder_path, const std::string& file_name_format)
{
	data_collector->Initialize(use_logging, (folder_path != "") ? gcnew String(folder_path.c_str()) : nullptr, (file_name_format != "") ? gcnew String(file_name_format.c_str()) : nullptr);
}

void DataCollectorImpl::Stop()
{
	data_collector->Stop();
}

void DataCollectorImpl::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
{
	data_collector->InitializeSystemMonitoring(is_cpu, is_free_ram);
}

void DataCollectorImpl::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
{
	data_collector->InitializeProcessMonitoring(is_cpu, is_memory, is_threads);
}

void DataCollectorImpl::InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads)
{
	data_collector->InitializeProcessMonitoring(gcnew String(process_name.c_str()), is_cpu, is_memory, is_threads);
}

void DataCollectorImpl::MonitoringServiceAlive()
{
	data_collector->MonitorServiceAlive();
}

HSMSensor<bool> DataCollectorImpl::CreateBoolSensor(const std::string& path, const std::string& description)
{
	auto bool_sensor = data_collector->CreateBoolSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<bool>{std::make_shared<HSMSensorImpl<bool>>(bool_sensor)};
}

HSMSensor<int> DataCollectorImpl::CreateIntSensor(const std::string& path, const std::string& description)
{
	auto int_sensor = data_collector->CreateIntSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<int>{std::make_shared<HSMSensorImpl<int>>(int_sensor)};
}

HSMSensor<double> DataCollectorImpl::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	auto double_sensor = data_collector->CreateDoubleSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<double>{std::make_shared<HSMSensorImpl<double>>(double_sensor)};
}

HSMSensor<const std::string&> DataCollectorImpl::CreateStringSensor(const std::string& path, const std::string& description)
{
	auto string_sensor = data_collector->CreateStringSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<const std::string&>{std::make_shared<HSMSensorImpl<const std::string&>>(string_sensor)};
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

HSMLastValueSensor<const std::string&> DataCollectorImpl::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	auto int_default_sensor = data_collector->CreateLastValueStringSensor(gcnew String(path.c_str()), gcnew String(default_value.c_str()), gcnew String(description.c_str()));
	return HSMLastValueSensor<const std::string&>{std::make_shared<HSMLastValueSensorImpl<const std::string&>>(int_default_sensor)};
}

HSMBarSensor<int> DataCollectorImpl::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	auto int_bar_sensor = data_collector->CreateIntBarSensor(gcnew String(path.c_str()), timeout, small_period, gcnew String(description.c_str()));
	return HSMBarSensor<int>{std::make_shared<HSMBarSensorImpl<int>>(int_bar_sensor)};
}

HSMBarSensor<double> DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	auto double_bar_sensor = data_collector->CreateDoubleBarSensor(gcnew String(path.c_str()), timeout, small_period, precision, gcnew String(description.c_str()));
	return HSMBarSensor<double>{std::make_shared<HSMBarSensorImpl<double>>(double_bar_sensor)};
}




DataCollectorImplWrapper::DataCollectorImplWrapper(const std::string& product_key, const std::string& address, int port) try : impl( std::make_shared<DataCollectorImpl>(product_key, address, port))
{
}
catch (System::Exception^ ex)
{
	throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
}

void DataCollectorImplWrapper::Initialize(bool use_logging, const std::string& folder_path, const std::string& file_name_format)
{
	try
	{
		impl->Initialize(use_logging, folder_path, file_name_format);
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

void DataCollectorImplWrapper::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
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

void DataCollectorImplWrapper::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
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

void DataCollectorImplWrapper::InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads)
{
	try
	{
		impl->InitializeProcessMonitoring(process_name, is_cpu, is_memory, is_threads);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

void DataCollectorImplWrapper::MonitoringServiceAlive()
{
	try
	{
		impl->MonitoringServiceAlive();
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

HSMSensor<const std::string&> DataCollectorImplWrapper::CreateStringSensor(const std::string& path, const std::string& description)
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




DataCollectorProxy::DataCollectorProxy(const std::string& product_key, const std::string& address, int port) : impl_wrapper(std::make_shared<DataCollectorImplWrapper>(product_key, address, port))
{
}

void DataCollectorProxy::Initialize(bool use_logging, const std::string& folder_path, const std::string& file_name_format)
{
	impl_wrapper->Initialize(use_logging, folder_path, file_name_format);
}

void DataCollectorProxy::Stop()
{
	impl_wrapper->Stop();
}

void DataCollectorProxy::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
{
	impl_wrapper->InitializeSystemMonitoring(is_cpu, is_free_ram);
}

void DataCollectorProxy::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
{
	impl_wrapper->InitializeProcessMonitoring(is_cpu, is_memory, is_threads);
}

void DataCollectorProxy::InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads)
{
	impl_wrapper->InitializeProcessMonitoring(process_name, is_cpu, is_memory, is_threads);
}

void DataCollectorProxy::MonitoringServiceAlive()
{
	impl_wrapper->MonitoringServiceAlive();
}


BoolSensor DataCollectorProxy::CreateBoolSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateBoolSensor(path, description);
}

IntSensor DataCollectorProxy::CreateIntSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateIntSensor(path, description);
}

DoubleSensor DataCollectorProxy::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateDoubleSensor(path, description);
}

StringSensor DataCollectorProxy::CreateStringSensor(const std::string& path, const std::string& description)
{
	return impl_wrapper->CreateStringSensor(path, description);
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

DoubleBarSensor DataCollectorProxy::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	return impl_wrapper->CreateDoubleBarSensor(path, timeout, small_period, precision, description);
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