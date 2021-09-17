#pragma once

#include <chrono>
#include <functional>

namespace hsm_wrapper
{
	template<class T>
	class HSMNoParamsFuncSensorImpl;

	template<class T>
	class HSMWRAPPER_API HSMNoParamsFuncSensorImplWrapper
	{
	public:
		using Type = T;

		HSMNoParamsFuncSensorImplWrapper(std::shared_ptr<HSMNoParamsFuncSensorImpl<T>> impl);

		void SetFunc(std::function<T()> parent_func);

		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);

		T Func();

	private:
		std::shared_ptr<HSMNoParamsFuncSensorImpl<T>> impl;
		std::function<T()> func;
	};

	template<class T>
	class HSMNoParamsFuncSensor
	{
	public:
		using Type = typename std::conditional<std::is_arithmetic_v<T>, T, std::string>::type;

		HSMNoParamsFuncSensor(std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<Type>> impl_wrapper) : impl_wrapper(impl_wrapper)
		{
		}

		std::chrono::milliseconds GetInterval()
		{
			impl_wrapper->GetInterval();
		}

		void RestartTimer(std::chrono::milliseconds time_interval)
		{
			impl_wrapper->RestartTimer(time_interval);
		}

	private:
		std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<Type>> impl_wrapper;
	};
}