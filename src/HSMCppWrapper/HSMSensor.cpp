#include "pch.h"

#include "HSMSensor.h"
#include "HSMSensorImpl.h"

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
	void HSMSensor<T>::AddValue(T value)
	{
		impl->AddValue(value);
	}

	template<class T>
	void HSMSensor<T>::AddValue(T value, const std::string& comment)
	{
		impl->AddValue(value, comment);
	}

	template<class T>
	void HSMSensor<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		impl->AddValue(value, status, comment);
	}




	template<class T>
	HSMSensorImpl<T>::HSMSensorImpl(typename SensorType<T>::type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(T value)
	{
		sensor->AddValue(value);
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(T value, const std::string& comment)
	{
		sensor->AddValue(value, gcnew String(comment.c_str()));
	}

	template<class T>
	void HSMSensorImpl<T>::AddValue(T value, HSMSensorStatus status, const std::string& comment)
	{
		sensor->AddValue(value, SensorStatus(status), gcnew String(comment.c_str()));
	}

	template<>
	void HSMSensorImpl<const std::string&>::AddValue(const std::string& value)
	{
		sensor->AddValue(gcnew String(value.c_str()));
	}

	template<>
	void HSMSensorImpl<const std::string&>::AddValue(const std::string& value, const std::string& comment)
	{
		sensor->AddValue(gcnew String(value.c_str()), gcnew String(comment.c_str()));
	}

	template<>
	void HSMSensorImpl<const std::string&>::AddValue(const std::string& value, HSMSensorStatus status, const std::string& comment)
	{
		sensor->AddValue(gcnew String(value.c_str()), SensorStatus(status), gcnew String(comment.c_str()));
	}


	template class HSMSensor<bool>;
	template class HSMSensorImpl<bool>;
	template class HSMSensor<int>;
	template class HSMSensorImpl<int>;
	template class HSMSensor<double>;
	template class HSMSensorImpl<double>;
	template class HSMSensor<const std::string&>;
	template class HSMSensorImpl<const std::string&>;
}