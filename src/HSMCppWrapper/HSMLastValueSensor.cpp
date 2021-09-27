#include "pch.h"

#include "HSMLastValueSensor.h"
#include "HSMLastValueSensorImpl.h"

#include "msclr/marshal_cppstd.h"

using namespace std;

using System::String;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

namespace hsm_wrapper
{
	template<class T>
	HSMLastValueSensor<T>::HSMLastValueSensor(std::shared_ptr<HSMLastValueSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value)
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
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value, const std::string& comment)
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
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
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
	HSMLastValueSensorImpl<T>::HSMLastValueSensorImpl(ILastValueSensor<ElementType>^ sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value);
		else
			sensor->AddValue(gcnew String(value.c_str()));
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value, gcnew String(comment.c_str()));
		else
			sensor->AddValue(gcnew String(value.c_str()), gcnew String(comment.c_str()));
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		if constexpr (std::is_arithmetic_v<T>)
			sensor->AddValue(value, SensorStatus(status), gcnew String(comment.c_str()));
		else
			sensor->AddValue(gcnew String(value.c_str()), SensorStatus(status), gcnew String(comment.c_str()));
	}



	
	template class HSMLastValueSensor<bool>;
	template class HSMLastValueSensorImpl<bool>;
	template class HSMLastValueSensor<int>;
	template class HSMLastValueSensorImpl<int>;
	template class HSMLastValueSensor<double>;
	template class HSMLastValueSensorImpl<double>;
	template class HSMLastValueSensor<string>;
	template class HSMLastValueSensorImpl<string>;
}