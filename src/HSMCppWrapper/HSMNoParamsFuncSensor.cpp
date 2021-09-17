#include "pch.h"

#include "HSMNoParamsFuncSensor.h"
#include "HSMNoParamsFuncSensorImpl.h"

using namespace std;
using namespace hsm_wrapper;

using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;

template<class T>
void HSMNoParamsFuncSensorImpl<T>::SetParamsFuncSensor(INoParamsFuncSensor<Type>^ new_sensor)
{
	sensor = new_sensor;
}

template<class T>
std::chrono::milliseconds HSMNoParamsFuncSensorImpl<T>::GetInterval()
{
	return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
}

template<class T>
void HSMNoParamsFuncSensorImpl<T>::RestartTimer(std::chrono::milliseconds time_interval)
{
	sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
}




template<class T>
HSMNoParamsFuncSensorImplWrapper<T>::HSMNoParamsFuncSensorImplWrapper(std::shared_ptr<HSMNoParamsFuncSensorImpl<T>> impl) : impl(impl)
{
}

template<class T>
void HSMNoParamsFuncSensorImplWrapper<T>::SetFunc(std::function<T()> function)
{
	func = function;
}

template<class T>
std::chrono::milliseconds HSMNoParamsFuncSensorImplWrapper<T>::GetInterval()
{
	try
	{
		return impl->GetInterval();
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

template<class T>
void HSMNoParamsFuncSensorImplWrapper<T>::RestartTimer(std::chrono::milliseconds time_interval)
{
	try
	{
		impl->RestartTimer(time_interval);
	}
	catch (System::Exception^ ex)
	{
		throw std::exception(msclr::interop::marshal_as<std::string>(ex->Message).c_str());
	}
}

template<class T>
T HSMNoParamsFuncSensorImplWrapper<T>::Func()
{
	return func();
}




#define InstantiateTemplates(X)\
template HSMNoParamsFuncSensorImplWrapper<X>;\
template HSMNoParamsFuncSensorImpl<X>;


InstantiateTemplates(int)
InstantiateTemplates(double)
InstantiateTemplates(bool)
InstantiateTemplates(string)