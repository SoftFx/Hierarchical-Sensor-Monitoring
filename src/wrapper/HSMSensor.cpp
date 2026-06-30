#include "pch.h"

#include "HSMSensor.h"
#include "HSMSensorImpl.h"

namespace hsm_wrapper
{
	// ---- HSMSensor<T> (public): forwards to the native pimpl. The C++/CLI build translated managed
	// exceptions to std::exception here; native hsm::collector::Error already IS a std::exception, so
	// the value path forwards directly and any failure propagates as std::exception unchanged.

	template<class T>
	HSMSensor<T>::HSMSensor(std::shared_ptr<HSMSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		impl->AddValue(value, comment);
	}

	template<class T>
	void HSMSensor<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		impl->AddValue(value, status, comment);
	}

	// ---- HSMSensorImpl<T> (native): forwards to the hsm::collector instant sensor. The native AddValue
	// signature is AddValue(value, SensorStatus = Ok, comment = ""); HSMSensorStatus has identical
	// enumerator values to hsm::collector::SensorStatus, so the cast is exact.

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor.AddValue(value);
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		sensor.AddValue(value, hsm::collector::SensorStatus::Ok, comment);
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		sensor.AddValue(value, static_cast<hsm::collector::SensorStatus>(status), comment);
	}

	template class HSMSensor<bool>;
	template class HSMSensorImpl<bool>;
	template class HSMSensor<int>;
	template class HSMSensorImpl<int>;
	template class HSMSensor<double>;
	template class HSMSensorImpl<double>;
	template class HSMSensor<std::string>;
	template class HSMSensorImpl<std::string>;
}
