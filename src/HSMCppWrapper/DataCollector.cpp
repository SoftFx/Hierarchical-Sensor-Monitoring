#include "pch.h"

#include "DataCollector.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMDefaultSensorImpl.h"

#include "msclr/auto_gcroot.h"
#include "msclr/marshal_cppstd.h"


using System::String;
using namespace HSMDataCollector::Core;
using namespace HSMDataCollector::PublicInterface;

using namespace std;
using namespace hsm_wrapper;


namespace hsm_wrapper
{
	class DataCollectorImpl
	{
	public:
		DataCollectorImpl(const std::string& product_key, const std::string& address, int port);

		void Initialize(bool use_logging, const std::string& folder_path, const std::string& file_name_format);
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
		msclr::auto_gcroot<IDataCollector^> data_collector;
	};
}

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

HSMSensor<bool> DataCollectorImpl::CreateBoolSensor(const std::string& path)
{
	IBoolSensor^ bool_sensor = data_collector->CreateBoolSensor(gcnew String(path.c_str()));
	return HSMSensor<bool>{std::make_shared<HSMSensorImpl<bool>>(bool_sensor)};
}

HSMSensor<double> DataCollectorImpl::CreateDoubleSensor(const std::string& path)
{
	IDoubleSensor^ double_sensor = data_collector->CreateDoubleSensor(gcnew String(path.c_str()));
	return HSMSensor<double>{std::make_shared<HSMSensorImpl<double>>(double_sensor)};
}

HSMSensor<int> DataCollectorImpl::CreateIntSensor(const std::string& path)
{
	IIntSensor^ int_sensor = data_collector->CreateIntSensor(gcnew String(path.c_str()));
	return HSMSensor<int>{std::make_shared<HSMSensorImpl<int>>(int_sensor)};
}

HSMSensor<const std::string&> DataCollectorImpl::CreateStringSensor(const std::string& path)
{
	IStringSensor^ string_sensor = data_collector->CreateStringSensor(gcnew String(path.c_str()));
	return HSMSensor<const std::string&>{std::make_shared<HSMSensorImpl<const std::string&>>(string_sensor)};
}

HSMDefaultSensor<double> DataCollectorImpl::CreateDefaultValueSensorDouble(const std::string& path, double default_value)
{
	IDefaultValueSensorDouble^ double_default_sensor = data_collector->CreateDefaultValueSensorDouble(gcnew String(path.c_str()), default_value);
	return HSMDefaultSensor<double>{std::make_shared<HSMDefaultSensorImpl<double>>(double_default_sensor)};
}

HSMDefaultSensor<int> DataCollectorImpl::CreateDefaultValueSensorInt(const std::string& path, int default_value)
{
	IDefaultValueSensorInt^ int_default_sensor = data_collector->CreateDefaultValueSensorInt(gcnew String(path.c_str()), default_value);
	return HSMDefaultSensor<int>{std::make_shared<HSMDefaultSensorImpl<int>>(int_default_sensor)};
}

HSMBarSensor<double> DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision)
{
	IDoubleBarSensor^ double_bar_sensor = data_collector->CreateDoubleBarSensor(gcnew String(path.c_str()), timeout, small_period, precision);
	return HSMBarSensor<double>{std::make_shared<HSMBarSensorImpl<double>>(double_bar_sensor)};
}

HSMBarSensor<int> DataCollectorImpl::CreateIntBarSensor(const std::string& path, int timeout, int small_period)
{
	IIntBarSensor^ int_bar_sensor = data_collector->CreateIntBarSensor(gcnew String(path.c_str()), timeout, small_period);
	return HSMBarSensor<int>{std::make_shared<HSMBarSensorImpl<int>>(int_bar_sensor)};
}




DataCollectorProxy::DataCollectorProxy(const std::string& product_key, const std::string& address, int port) try : impl( std::make_shared<DataCollectorImpl>(product_key, address, port))
{
}
catch (System::Exception^ ex)
{
	throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
}

void DataCollectorProxy::Initialize(bool use_logging, const std::string& folder_path, const std::string& file_name_format)
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

void DataCollectorProxy::Stop()
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

void DataCollectorProxy::InitializeSystemMonitoring(bool is_cpu, bool is_free_ram)
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

void DataCollectorProxy::InitializeProcessMonitoring(bool is_cpu, bool is_memory, bool is_threads)
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

void DataCollectorProxy::InitializeProcessMonitoring(const std::string& process_name, bool is_cpu, bool is_memory, bool is_threads)
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

void DataCollectorProxy::MonitoringServiceAlive()
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

HSMSensor<bool> DataCollectorProxy::CreateBoolSensor(const std::string& path)
{
	try
	{
		return impl->CreateBoolSensor(path);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMSensor<double> DataCollectorProxy::CreateDoubleSensor(const std::string& path)
{
	try
	{
		return impl->CreateDoubleSensor(path);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMSensor<int> DataCollectorProxy::CreateIntSensor(const std::string& path)
{
	try
	{
		return impl->CreateIntSensor(path);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMSensor<const std::string&> DataCollectorProxy::CreateStringSensor(const std::string& path)
{
	try
	{
		return impl->CreateStringSensor(path);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMDefaultSensor<double> DataCollectorProxy::CreateDefaultValueSensorDouble(const std::string& path, double default_value)
{
	try
	{
		return impl->CreateDefaultValueSensorDouble(path, default_value);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMDefaultSensor<int> DataCollectorProxy::CreateDefaultValueSensorInt(const std::string& path, int default_value)
{
	try
	{
		return impl->CreateDefaultValueSensorInt(path, default_value);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMBarSensor<double> DataCollectorProxy::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision)
{
	try
	{
		return impl->CreateDoubleBarSensor(path, timeout, small_period, precision);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

HSMBarSensor<int> DataCollectorProxy::CreateIntBarSensor(const std::string& path, int timeout, int small_period)
{
	try
	{
		return impl->CreateIntBarSensor(path, timeout, small_period);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}