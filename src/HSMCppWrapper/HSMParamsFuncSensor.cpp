#include "pch.h"

#include "HSMParamsFuncSensor.h"
#include "HSMParamsFuncSensorImpl.h"

using namespace std;
using namespace hsm_wrapper;

using namespace HSMDataCollector::PublicInterface;
using namespace HSMSensorDataObjects;


template<class T, class U>
void HSMParamsFuncSensorImpl<T, U>::SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor)
{
	sensor = new_sensor;
}

template<class T, class U>
std::chrono::milliseconds HSMParamsFuncSensorImpl<T, U>::GetInterval()
{
	return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
}

template<class T, class U>
void HSMParamsFuncSensorImpl<T, U>::RestartTimer(std::chrono::milliseconds time_interval)
{
	sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
}

template<class T, class U>
void HSMParamsFuncSensorImpl<T, U>::AddValue(ElementParameterType value)
{
	if constexpr (std::is_arithmetic_v<U>)
		sensor->AddValue(value);
	else
		sensor->AddValue(gcnew String(value.c_str()));
}




template<class T, class U>
HSMParamsFuncSensorImplWrapper<T, U>::HSMParamsFuncSensorImplWrapper(std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> impl) : impl(impl)
{
}

template<class T, class U>
void HSMParamsFuncSensorImplWrapper<T, U>::SetFunc(std::function<T(std::list<U>)> function)
{
	func = function;
}

template<class T, class U>
std::chrono::milliseconds HSMParamsFuncSensorImplWrapper<T, U>::GetInterval()
{
	return impl->GetInterval();
}

template<class T, class U>
void HSMParamsFuncSensorImplWrapper<T, U>::RestartTimer(std::chrono::milliseconds time_interval)
{
	impl->RestartTimer(time_interval);
}

template<class T, class U>
void HSMParamsFuncSensorImplWrapper<T, U>::AddValue(U value)
{
	impl->AddValue(value);
}

template<class T, class U>
T HSMParamsFuncSensorImplWrapper<T, U>::Func(const std::list<U>& values)
{
	return func(values);
}




#define InstantiateTemplates(X, Y)\
template HSMParamsFuncSensorImplWrapper<X, Y>;\
template HSMParamsFuncSensorImpl<X, Y>;


InstantiateTemplates(int, int)
InstantiateTemplates(int, double)
InstantiateTemplates(int, bool)
InstantiateTemplates(int, string)

InstantiateTemplates(double, int)
InstantiateTemplates(double, double)
InstantiateTemplates(double, bool)
InstantiateTemplates(double, string)

InstantiateTemplates(bool, int)
InstantiateTemplates(bool, double)
InstantiateTemplates(bool, bool)
InstantiateTemplates(bool, string)

InstantiateTemplates(string, int)
InstantiateTemplates(string, double)
InstantiateTemplates(string, bool)
InstantiateTemplates(string, string)