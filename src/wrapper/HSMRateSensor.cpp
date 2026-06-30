#include "pch.h"

#include "HSMRateSensor.h"
#include "HSMRateSensorImpl.h"

namespace hsm_wrapper
{
	template<class T>
	HSMRateSensor<T>::HSMRateSensor(std::shared_ptr<HSMRateSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMRateSensor<T>::AddValue(ElementParameterType value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMRateSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor.AddValue(static_cast<double>(value));
	}

	template class HSMRateSensor<int>;
	template class HSMRateSensorImpl<int>;
	template class HSMRateSensor<double>;
	template class HSMRateSensorImpl<double>;
}
