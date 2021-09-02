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




template HSMParamsFuncSensorImplWrapper<int, int>;
template HSMParamsFuncSensorImpl<int, int>;

template HSMParamsFuncSensorImplWrapper<int, double>;
template HSMParamsFuncSensorImpl<int, double>;

template HSMParamsFuncSensorImplWrapper<int, bool>;
template HSMParamsFuncSensorImpl<int, bool>;

template HSMParamsFuncSensorImplWrapper<int, string>;
template HSMParamsFuncSensorImpl<int, string>;

template HSMParamsFuncSensorImplWrapper<double, int>;
template HSMParamsFuncSensorImpl<double, int>;

template HSMParamsFuncSensorImplWrapper<double, double>;
template HSMParamsFuncSensorImpl<double, double>;

template HSMParamsFuncSensorImplWrapper<double, bool>;
template HSMParamsFuncSensorImpl<double, bool>;

template HSMParamsFuncSensorImplWrapper<double, string>;
template HSMParamsFuncSensorImpl<double, string>;

template HSMParamsFuncSensorImplWrapper<bool, int>;
template HSMParamsFuncSensorImpl<bool, int>;

template HSMParamsFuncSensorImplWrapper<bool, double>;
template HSMParamsFuncSensorImpl<bool, double>;

template HSMParamsFuncSensorImplWrapper<bool, bool>;
template HSMParamsFuncSensorImpl<bool, bool>;

template HSMParamsFuncSensorImplWrapper<bool, string>;
template HSMParamsFuncSensorImpl<bool, string>;

template HSMParamsFuncSensorImplWrapper<string, int>;
template HSMParamsFuncSensorImpl<string, int>;

template HSMParamsFuncSensorImplWrapper<string, double>;
template HSMParamsFuncSensorImpl<string, double>;

template HSMParamsFuncSensorImplWrapper<string, bool>;
template HSMParamsFuncSensorImpl<string, bool>;

template HSMParamsFuncSensorImplWrapper<string, string>;
template HSMParamsFuncSensorImpl<string, string>;
