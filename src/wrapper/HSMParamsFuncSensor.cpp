#include "pch.h"

#include "HSMParamsFuncSensor.h"
#include "HSMParamsFuncSensorImpl.h"

namespace hsm_wrapper
{
	template<class T, class U>
	void HSMParamsFuncSensorImpl<T, U>::SetParamsFuncSensor(hsm::collector::ValuesFunctionSensor new_sensor, std::chrono::milliseconds new_interval)
	{
		sensor = std::move(new_sensor);
		interval = new_interval;
	}

	template<class T, class U>
	std::chrono::milliseconds HSMParamsFuncSensorImpl<T, U>::GetInterval()
	{
		return interval;
	}

	template<class T, class U>
	void HSMParamsFuncSensorImpl<T, U>::RestartTimer(std::chrono::milliseconds time_interval)
	{
		// Fixed native period (see the no-params impl); record but do not re-arm.
		interval = time_interval;
	}

	template<class T, class U>
	void HSMParamsFuncSensorImpl<T, U>::AddValue(ElementParameterType value)
	{
		if constexpr (std::is_arithmetic_v<U>)
			sensor.AddValue(static_cast<std::int32_t>(value));
		else
			throw hsm::collector::Error("Non-arithmetic values-function elements are not supported by the native collector (int-only).");
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

#define InstantiateTemplates(X, Y)                              \
	template class HSMParamsFuncSensorImplWrapper<X, Y>;         \
	template class HSMParamsFuncSensorImpl<X, Y>;

	InstantiateTemplates(int, int)
	InstantiateTemplates(int, double)
	InstantiateTemplates(int, bool)
	InstantiateTemplates(int, std::string)

	InstantiateTemplates(double, int)
	InstantiateTemplates(double, double)
	InstantiateTemplates(double, bool)
	InstantiateTemplates(double, std::string)

	InstantiateTemplates(bool, int)
	InstantiateTemplates(bool, double)
	InstantiateTemplates(bool, bool)
	InstantiateTemplates(bool, std::string)

	InstantiateTemplates(std::string, int)
	InstantiateTemplates(std::string, double)
	InstantiateTemplates(std::string, bool)
	InstantiateTemplates(std::string, std::string)
#undef InstantiateTemplates
}
