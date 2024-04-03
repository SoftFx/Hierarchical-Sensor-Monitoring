#include "pch.h"

#include "HSMRateSensor.h"
#include "HSMRateSensorImpl.h"

#include "msclr/marshal_cppstd.h"

using namespace std;

using System::String;
using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

namespace hsm_wrapper
{
	template<class T>
	HSMRateSensor<T>::HSMRateSensor(std::shared_ptr<HSMRateSensorImpl<T>> sensor_impl) : impl(sensor_impl)
	{
	}

	template<class T>
	void HSMRateSensor<T>::AddValue(ElementParameterType value)
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
	HSMRateSensorImpl<T>::HSMRateSensorImpl(typename RateSensorType<T>::Type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMRateSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor->AddValue((double)value);
	}



	template class HSMRateSensor<int>;
	template class HSMRateSensorImpl<int>;
	template class HSMRateSensor<double>;
	template class HSMRateSensorImpl<double>;
}