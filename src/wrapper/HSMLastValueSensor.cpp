#include "pch.h"

#include "HSMLastValueSensor.h"
#include "HSMLastValueSensorImpl.h"

namespace hsm_wrapper
{
	template<class T>
	HSMLastValueSensor<T>::HSMLastValueSensor(std::shared_ptr<HSMLastValueSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		impl->AddValue(value, comment);
	}

	template<class T>
	void HSMLastValueSensor<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		impl->AddValue(value, status, comment);
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor.AddValue(value);
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value, const std::string& comment)
	{
		sensor.AddValue(value, hsm::collector::SensorStatus::Ok, comment);
	}

	template<class T>
	void HSMLastValueSensorImpl<T>::AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment)
	{
		sensor.AddValue(value, static_cast<hsm::collector::SensorStatus>(status), comment);
	}

	template class HSMLastValueSensor<bool>;
	template class HSMLastValueSensorImpl<bool>;
	template class HSMLastValueSensor<int>;
	template class HSMLastValueSensorImpl<int>;
	template class HSMLastValueSensor<double>;
	template class HSMLastValueSensorImpl<double>;
	template class HSMLastValueSensor<std::string>;
	template class HSMLastValueSensorImpl<std::string>;
}
