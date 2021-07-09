#include "pch.h"

#include "HSMDefaultSensor.h"
#include "HSMDefaultSensorImpl.h"

using namespace std;

using System::String;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

namespace hsm_wrapper
{
	template<class T>
	HSMDefaultSensor<T>::HSMDefaultSensor(std::shared_ptr<HSMDefaultSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	HSMDefaultSensor<T>::HSMDefaultSensor(HSMDefaultSensor&& sensor) : impl(sensor.impl)
	{
		sensor.impl = nullptr;
	}

	template<class T>
	void HSMDefaultSensor<T>::AddValue(T value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMDefaultSensor<T>::AddValue(T value, const std::string& comment)
	{
		impl->AddValue(value, comment);
	}

	template<class T>
	void HSMDefaultSensor<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		impl->AddValue(value, status, comment);
	}




	template<class T>
	HSMDefaultSensorImpl<T>::HSMDefaultSensorImpl(typename DefaultSensorType<T>::type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMDefaultSensorImpl<T>::AddValue(T value)
	{
		sensor->AddValue(value);
	}

	template<class T>
	void HSMDefaultSensorImpl<T>::AddValue(T value, const std::string& comment)
	{
		sensor->AddValue(value, gcnew String(comment.c_str()));
	}

	template<class T>
	void HSMDefaultSensorImpl<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		sensor->AddValue(value, SensorStatus(status), gcnew String(comment.c_str()));
	}




	template class HSMDefaultSensor<int>;
	template class HSMDefaultSensorImpl<int>;
	template class HSMDefaultSensor<double>;
	template class HSMDefaultSensorImpl<double>;
}