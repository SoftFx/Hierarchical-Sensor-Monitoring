#include "pch.h"

#include "HSMLastValueSensor.h"
#include "HSMLastValueSensorImpl.h"

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
	HSMLastValueSensor<T>::HSMLastValueSensor(HSMLastValueSensor&& sensor) : impl(sensor.impl)
	{
		sensor.impl = nullptr;
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(T value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(T value, const std::string& comment)
	{
		impl->AddValue(value, comment);
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		impl->AddValue(value, status, comment);
	}




	template<class T>
	HSMLastValueSensorImpl<T>::HSMLastValueSensorImpl(typename DefaultSensorType<T>::type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(T value)
	{
		sensor->AddValue(value);
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(T value, const std::string& comment)
	{
		sensor->AddValue(value, gcnew String(comment.c_str()));
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		sensor->AddValue(value, SensorStatus(status), gcnew String(comment.c_str()));
	}

	template<>
	void HSMLastValueSensorImpl<const std::string&>::AddValue(const std::string& value)
	{
		sensor->AddValue(gcnew String(value.c_str()));
	}

	template<>
	void HSMLastValueSensorImpl<const std::string&>::AddValue(const std::string& value, const std::string& comment)
	{
		sensor->AddValue(gcnew String(value.c_str()), gcnew String(comment.c_str()));
	}

	template<>
	void HSMLastValueSensorImpl<const std::string&>::AddValue(const std::string& value, HSMSensorStatus status, const std::string& comment)
	{
		sensor->AddValue(gcnew String(value.c_str()), SensorStatus(status), gcnew String(comment.c_str()));
	}



	
	template class HSMLastValueSensor<bool>;
	template class HSMLastValueSensorImpl<bool>;
	template class HSMLastValueSensor<int>;
	template class HSMLastValueSensorImpl<int>;
	template class HSMLastValueSensor<double>;
	template class HSMLastValueSensorImpl<double>;
	template class HSMLastValueSensor<const std::string&>;
	template class HSMLastValueSensorImpl<const std::string&>;
}