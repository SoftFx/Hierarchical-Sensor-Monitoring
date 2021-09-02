#include "pch.h"

#include "DataCollector.h"
#include "HSMSensorImpl.h"
#include "HSMBarSensorImpl.h"
#include "HSMLastValueSensorImpl.h"
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

		
		HSMSensor<bool> CreateBoolSensor(const std::string& path, const std::string& description = "");
		HSMSensor<int> CreateIntSensor(const std::string& path, const std::string& description = "");
		HSMSensor<double> CreateDoubleSensor(const std::string& path, const std::string& description = "");
		HSMSensor<const std::string&> CreateStringSensor(const std::string& path, const std::string& description = "");
		HSMLastValueSensor<bool> CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description = "");
		HSMLastValueSensor<int> CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description = "");
		HSMLastValueSensor<double> CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description = "");
		HSMLastValueSensor<const std::string&> CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description = "");
		HSMBarSensor<int> CreateIntBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, const std::string& description = "");
		HSMBarSensor<double> CreateDoubleBarSensor(const std::string& path, int timeout = 300000, int small_period = 15000, int precision = 2, const std::string& description = "");
		
		template<class T, class U>
		typename std::enable_if_t<std::is_arithmetic_v<T> && std::is_arithmetic_v<U>, shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>>  
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> params_func_sensor_impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
			params_func_sensor_impl->SetFunc(function);
			DelegateWrapper<T, U>^ delegate_wrapper = params_func_sensor_impl->GetDelegateWrapper();
			IParamsFuncSensor<T, U>^ params_func_sensor = data_collector->CreateParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()), gcnew Func<List<U>^, T>(delegate_wrapper, &DelegateWrapper<T, U>::Call), interval.count());
			params_func_sensor_impl->SetParamsFuncSensor(params_func_sensor);
			std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> params_func_sensor_impl_wrapper = std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(params_func_sensor_impl);
			return params_func_sensor_impl_wrapper;
		}

		template<class T, class U>
		typename std::enable_if_t<std::is_same_v<T, std::string> && std::is_arithmetic_v<U>, shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>>
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> params_func_sensor_impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
			params_func_sensor_impl->SetFunc(function);
			DelegateWrapper<T, U>^ delegate_wrapper = params_func_sensor_impl->GetDelegateWrapper();
			IParamsFuncSensor<String^, U>^ params_func_sensor = data_collector->CreateParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()), gcnew Func<List<U>^, String^>(delegate_wrapper, &DelegateWrapper<T, U>::Call), interval.count());
			params_func_sensor_impl->SetParamsFuncSensor(params_func_sensor);
			std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> params_func_sensor_impl_wrapper = std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(params_func_sensor_impl);
			return params_func_sensor_impl_wrapper;
		}

		template<class T, class U>
		typename std::enable_if_t<std::is_arithmetic_v<T> && std::is_same_v<U, std::string>, shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>>
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> params_func_sensor_impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
			params_func_sensor_impl->SetFunc(function);
			DelegateWrapper<T, U>^ delegate_wrapper = params_func_sensor_impl->GetDelegateWrapper();
			IParamsFuncSensor<T, String^>^ params_func_sensor = data_collector->CreateParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()), gcnew Func<List<String^>^, T>(delegate_wrapper, &DelegateWrapper<T, U>::Call), interval.count());
			params_func_sensor_impl->SetParamsFuncSensor(params_func_sensor);
			std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> params_func_sensor_impl_wrapper = std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(params_func_sensor_impl);
			return params_func_sensor_impl_wrapper;
		}

		template<class T, class U>
		typename std::enable_if_t<std::is_same_v<T, std::string> && std::is_same_v<U, std::string>, shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>>>
			CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
		{
			std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> params_func_sensor_impl = std::make_shared<HSMParamsFuncSensorImpl<T, U>>();
			params_func_sensor_impl->SetFunc(function);
			DelegateWrapper<T, U>^ delegate_wrapper = params_func_sensor_impl->GetDelegateWrapper();
			IParamsFuncSensor<String^, String^>^ params_func_sensor = data_collector->CreateParamsFuncSensor(gcnew String(path.c_str()), gcnew String(description.c_str()), gcnew Func<List<String^>^, String^>(delegate_wrapper, &DelegateWrapper<T, U>::Call), interval.count());
			params_func_sensor_impl->SetParamsFuncSensor(params_func_sensor);
			std::shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> params_func_sensor_impl_wrapper = std::make_shared<HSMParamsFuncSensorImplWrapper<T, U>>(params_func_sensor_impl);
			return params_func_sensor_impl_wrapper;
		}

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

HSMSensor<bool> DataCollectorImpl::CreateBoolSensor(const std::string& path, const std::string& description)
{
	IInstantValueSensor<bool>^ bool_sensor = data_collector->CreateBoolSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<bool>{std::make_shared<HSMSensorImpl<bool>>(bool_sensor)};
}

HSMSensor<int> DataCollectorImpl::CreateIntSensor(const std::string& path, const std::string& description)
{
	IInstantValueSensor<int>^ int_sensor = data_collector->CreateIntSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<int>{std::make_shared<HSMSensorImpl<int>>(int_sensor)};
}

HSMSensor<double> DataCollectorImpl::CreateDoubleSensor(const std::string& path, const std::string& description)
{
	IInstantValueSensor<double>^ double_sensor = data_collector->CreateDoubleSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<double>{std::make_shared<HSMSensorImpl<double>>(double_sensor)};
}

HSMSensor<const std::string&> DataCollectorImpl::CreateStringSensor(const std::string& path, const std::string& description)
{
	IInstantValueSensor<String^>^ string_sensor = data_collector->CreateStringSensor(gcnew String(path.c_str()), gcnew String(description.c_str()));
	return HSMSensor<const std::string&>{std::make_shared<HSMSensorImpl<const std::string&>>(string_sensor)};
}

HSMLastValueSensor<bool> DataCollectorImpl::CreateLastValueBoolSensor(const std::string& path, bool default_value, const std::string& description)
{
	ILastValueSensor<bool>^ int_default_sensor = data_collector->CreateLastValueBoolSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<bool>{std::make_shared<HSMLastValueSensorImpl<bool>>(int_default_sensor)};
}

HSMLastValueSensor<int> DataCollectorImpl::CreateLastValueIntSensor(const std::string& path, int default_value, const std::string& description)
{
	ILastValueSensor<int>^ int_default_sensor = data_collector->CreateLastValueIntSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<int>{std::make_shared<HSMLastValueSensorImpl<int>>(int_default_sensor)};
}

HSMLastValueSensor<double> DataCollectorImpl::CreateLastValueDoubleSensor(const std::string& path, double default_value, const std::string& description)
{
	ILastValueSensor<double>^ double_default_sensor = data_collector->CreateLastValueDoubleSensor(gcnew String(path.c_str()), default_value, gcnew String(description.c_str()));
	return HSMLastValueSensor<double>{std::make_shared<HSMLastValueSensorImpl<double>>(double_default_sensor)};
}

HSMLastValueSensor<const std::string&> DataCollectorImpl::CreateLastValueStringSensor(const std::string& path, const std::string& default_value, const std::string& description)
{
	ILastValueSensor<String^>^ int_default_sensor = data_collector->CreateLastValueStringSensor(gcnew String(path.c_str()), gcnew String(default_value.c_str()), gcnew String(description.c_str()));
	return HSMLastValueSensor<const std::string&>{std::make_shared<HSMLastValueSensorImpl<const std::string&>>(int_default_sensor)};
}

HSMBarSensor<int> DataCollectorImpl::CreateIntBarSensor(const std::string& path, int timeout, int small_period, const std::string& description)
{
	IBarSensor<int>^ int_bar_sensor = data_collector->CreateIntBarSensor(gcnew String(path.c_str()), timeout, small_period, gcnew String(description.c_str()));
	return HSMBarSensor<int>{std::make_shared<HSMBarSensorImpl<int>>(int_bar_sensor)};
}

HSMBarSensor<double> DataCollectorImpl::CreateDoubleBarSensor(const std::string& path, int timeout, int small_period, int precision, const std::string& description)
{
	IBarSensor<double>^ double_bar_sensor = data_collector->CreateDoubleBarSensor(gcnew String(path.c_str()), timeout, small_period, precision, gcnew String(description.c_str()));
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

template<class T, class U>
shared_ptr<HSMParamsFuncSensorImplWrapper<T, U>> DataCollectorImplWrapper::CreateParamsFuncSensor(const std::string& path, const std::string& description, std::function<T(const std::list<U>&)> function, const std::chrono::milliseconds& interval)
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




template shared_ptr<HSMParamsFuncSensorImplWrapper<int, int>> DataCollectorImpl::CreateParamsFuncSensor<int, int>(const std::string& path, const std::string& description, function<int(const list<int>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<int, int>> DataCollectorImplWrapper::CreateParamsFuncSensor<int, int>(const std::string& path, const std::string& description, function<int(const list<int>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<int, double>> DataCollectorImpl::CreateParamsFuncSensor<int, double>(const std::string& path, const std::string& description, function<int(const list<double>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<int, double>> DataCollectorImplWrapper::CreateParamsFuncSensor<int, double>(const std::string& path, const std::string& description, function<int(const list<double>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<int, bool>> DataCollectorImpl::CreateParamsFuncSensor<int, bool>(const std::string& path, const std::string& description, function<int(const list<bool>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<int, bool>> DataCollectorImplWrapper::CreateParamsFuncSensor<int, bool>(const std::string& path, const std::string& description, function<int(const list<bool>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<int, string>> DataCollectorImpl::CreateParamsFuncSensor<int, string>(const std::string& path, const std::string& description, function<int(const list<string>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<int, string>> DataCollectorImplWrapper::CreateParamsFuncSensor<int, string>(const std::string& path, const std::string& description, function<int(const list<string>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<double, int>> DataCollectorImpl::CreateParamsFuncSensor<double, int>(const std::string& path, const std::string& description, function<double(const list<int>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<double, int>> DataCollectorImplWrapper::CreateParamsFuncSensor<double, int>(const std::string& path, const std::string& description, function<double(const list<int>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<double, double>> DataCollectorImpl::CreateParamsFuncSensor<double, double>(const std::string& path, const std::string& description, function<double(const list<double>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<double, double>> DataCollectorImplWrapper::CreateParamsFuncSensor<double, double>(const std::string& path, const std::string& description, function<double(const list<double>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<double, bool>> DataCollectorImpl::CreateParamsFuncSensor<double, bool>(const std::string& path, const std::string& description, function<double(const list<bool>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<double, bool>> DataCollectorImplWrapper::CreateParamsFuncSensor<double, bool>(const std::string& path, const std::string& description, function<double(const list<bool>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<double, string>> DataCollectorImpl::CreateParamsFuncSensor<double, string>(const std::string& path, const std::string& description, function<double(const list<string>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<double, string>> DataCollectorImplWrapper::CreateParamsFuncSensor<double, string>(const std::string& path, const std::string& description, function<double(const list<string>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<bool, int>> DataCollectorImpl::CreateParamsFuncSensor<bool, int>(const std::string& path, const std::string& description, function<bool(const list<int>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<bool, int>> DataCollectorImplWrapper::CreateParamsFuncSensor<bool, int>(const std::string& path, const std::string& description, function<bool(const list<int>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<bool, double>> DataCollectorImpl::CreateParamsFuncSensor<bool, double>(const std::string& path, const std::string& description, function<bool(const list<double>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<bool, double>> DataCollectorImplWrapper::CreateParamsFuncSensor<bool, double>(const std::string& path, const std::string& description, function<bool(const list<double>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<bool, bool>> DataCollectorImpl::CreateParamsFuncSensor<bool, bool>(const std::string& path, const std::string& description, function<bool(const list<bool>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<bool, bool>> DataCollectorImplWrapper::CreateParamsFuncSensor<bool, bool>(const std::string& path, const std::string& description, function<bool(const list<bool>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<bool, string>> DataCollectorImpl::CreateParamsFuncSensor<bool, string>(const std::string& path, const std::string& description, function<bool(const list<string>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<bool, string>> DataCollectorImplWrapper::CreateParamsFuncSensor<bool, string>(const std::string& path, const std::string& description, function<bool(const list<string>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<string, int>> DataCollectorImpl::CreateParamsFuncSensor<string, int>(const std::string& path, const std::string& description, function<string(const list<int>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<string, int>> DataCollectorImplWrapper::CreateParamsFuncSensor<string, int>(const std::string& path, const std::string& description, function<string(const list<int>&)> function, const std::chrono::milliseconds& interval);

template shared_ptr<HSMParamsFuncSensorImplWrapper<string, double>> DataCollectorImpl::CreateParamsFuncSensor<string, double>(const std::string& path, const std::string& description, function<string(const list<double>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<string, double>> DataCollectorImplWrapper::CreateParamsFuncSensor<string, double>(const std::string& path, const std::string& description, function<string(const list<double>&)> function, const std::chrono::milliseconds& intervals);

template shared_ptr<HSMParamsFuncSensorImplWrapper<string, bool>> DataCollectorImpl::CreateParamsFuncSensor<string, bool>(const std::string& path, const std::string& description, function<string(const list<bool>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<string, bool>> DataCollectorImplWrapper::CreateParamsFuncSensor<string, bool>(const std::string& path, const std::string& description, function<string(const list<bool>&)> function, const std::chrono::milliseconds& intervals);

template shared_ptr<HSMParamsFuncSensorImplWrapper<string, string>> DataCollectorImpl::CreateParamsFuncSensor<string, string>(const std::string& path, const std::string& description, function<string(const list<string>&)> function, const std::chrono::milliseconds& interval);
template HSMWRAPPER_API shared_ptr<HSMParamsFuncSensorImplWrapper<string, string>> DataCollectorImplWrapper::CreateParamsFuncSensor<string, string>(const std::string& path, const std::string& description, function<string(const list<string>&)> function, const std::chrono::milliseconds& interval);