#include "pch.h"

#include "HSMSensor.h"
#include "HSMSensorImpl.h"

#include "msclr/marshal_cppstd.h"

using namespace std;

using System::String;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

namespace hsm_wrapper
{
	template<class T>
	HSMSensor<T>::HSMSensor(std::shared_ptr<HSMSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value)
	{
		try
		{
			impl->AddValue(value);
		}
		catch (System::Exception^ ex)
		{
			throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
		}
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		try
		{
			impl->AddValue(value, comment);
		}
		catch (System::Exception^ ex)
		{
			throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
		}
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		try
		{
			impl->AddValue(value, status, comment);
		}
		catch (System::Exception^ ex)
		{
			throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
		}
	}




	template<class T>
	HSMSensorImpl<T>::HSMSensorImpl(IInstantValueSensor<ElementType>^ sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value);
		else
			sensor->AddValue(gcnew String(value.c_str()));
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value, gcnew String(comment.c_str()));
		else
			sensor->AddValue(gcnew String(value.c_str()), gcnew String(comment.c_str()));
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value, SensorStatus(status), gcnew String(comment.c_str()));
		else
			sensor->AddValue(gcnew String(value.c_str()), SensorStatus(status), gcnew String(comment.c_str()));
	}




	template class HSMSensor<bool>;
	template class HSMSensorImpl<bool>;
	template class HSMSensor<int>;
	template class HSMSensorImpl<int>;
	template class HSMSensor<double>;
	template class HSMSensorImpl<double>;
	template class HSMSensor<string>;
	template class HSMSensorImpl<string>;
}