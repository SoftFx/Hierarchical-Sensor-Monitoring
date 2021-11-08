#include "pch.h"

#include "HSMBarSensor.h"
#include "HSMBarSensorImpl.h"

#include "msclr/marshal_cppstd.h"

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
	void HSMBarSensor<T>::AddValue(ElementParameterType value)
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
	HSMBarSensorImpl<T>::HSMBarSensorImpl(typename BarSensorType<T>::Type sensor) : sensor(sensor)
	{
	}

	template<class T>
	void HSMBarSensorImpl<T>::AddValue(ElementParameterType value)
	{
		sensor->AddValue(value);
	}



	template class HSMBarSensor<int>;
	template class HSMBarSensorImpl<int>;
	template class HSMBarSensor<double>;
	template class HSMBarSensorImpl<double>;
}