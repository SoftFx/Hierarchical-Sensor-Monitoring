#include "pch.h"

#include "HSMBarSensor.h"
#include "HSMBarSensorImpl.h"

using namespace std;

using System::String;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

namespace hsm_wrapper
{
	template<class T>
	HSMBarSensor<T>::HSMBarSensor(std::shared_ptr<HSMBarSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	HSMBarSensor<T>::HSMBarSensor(HSMBarSensor&& sensor) : impl(sensor.impl)
	{
		sensor.impl = nullptr;
	}

	template<class T>
	void HSMBarSensor<T>::AddValue(T value)
	{
		impl->AddValue(value);
	}





	template<class T>
	HSMBarSensorImpl<T>::HSMBarSensorImpl(typename BarSensorType<T>::type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMBarSensorImpl<T>::AddValue(T value)
	{
		sensor->AddValue(value);
	}



	template class HSMBarSensor<int>;
	template class HSMBarSensorImpl<int>;
	template class HSMBarSensor<double>;
	template class HSMBarSensorImpl<double>;
}