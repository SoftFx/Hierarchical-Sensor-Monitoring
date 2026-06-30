#include "pch.h"

#include "HSMBarSensor.h"
#include "HSMBarSensorImpl.h"

namespace hsm_wrapper
{
	template<class T>
	HSMBarSensor<T>::HSMBarSensor(std::shared_ptr<HSMBarSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMBarSensor<T>::AddValue(ElementParameterType value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMBarSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor.AddValue(value);
	}

	template class HSMBarSensor<int>;
	template class HSMBarSensorImpl<int>;
	template class HSMBarSensor<double>;
	template class HSMBarSensorImpl<double>;
}
