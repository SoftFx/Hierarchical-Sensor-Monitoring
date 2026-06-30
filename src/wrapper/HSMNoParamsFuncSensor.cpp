#include "pch.h"

#include "HSMNoParamsFuncSensor.h"
#include "HSMNoParamsFuncSensorImpl.h"

namespace hsm_wrapper
{
	template<class T>
	void HSMNoParamsFuncSensorImpl<T>::SetParamsFuncSensor(hsm::collector::FunctionSensor new_sensor, std::chrono::milliseconds new_interval)
	{
		sensor = std::move(new_sensor);
		interval = new_interval;
	}

	template<class T>
	std::chrono::milliseconds HSMNoParamsFuncSensorImpl<T>::GetInterval()
	{
		return interval;
	}

	template<class T>
	void HSMNoParamsFuncSensorImpl<T>::RestartTimer(std::chrono::milliseconds /*time_interval*/)
	{
		// The native function sensor's post period is fixed at creation and cannot be re-armed. This is
		// a true no-op: deliberately NOT caching the requested interval, so GetInterval keeps reporting
		// the actual (creation-time) period rather than echoing a value that never took effect.
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
		return impl->GetInterval();
	}

	template<class T>
	void HSMNoParamsFuncSensorImplWrapper<T>::RestartTimer(std::chrono::milliseconds time_interval)
	{
		impl->RestartTimer(time_interval);
	}

	template<class T>
	T HSMNoParamsFuncSensorImplWrapper<T>::Func()
	{
		return func();
	}

	template class HSMNoParamsFuncSensorImplWrapper<int>;
	template class HSMNoParamsFuncSensorImpl<int>;
	template class HSMNoParamsFuncSensorImplWrapper<double>;
	template class HSMNoParamsFuncSensorImpl<double>;
	template class HSMNoParamsFuncSensorImplWrapper<bool>;
	template class HSMNoParamsFuncSensorImpl<bool>;
	template class HSMNoParamsFuncSensorImplWrapper<std::string>;
	template class HSMNoParamsFuncSensorImpl<std::string>;
}
