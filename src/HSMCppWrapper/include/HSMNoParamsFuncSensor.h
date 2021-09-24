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
		using ResultType = T;

		HSMNoParamsFuncSensorImplWrapper(std::shared_ptr<HSMNoParamsFuncSensorImpl<T>> impl);

		void SetFunc(std::function<T()> parent_func);

		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);

		T Func();

	private:
		std::shared_ptr<HSMNoParamsFuncSensorImpl<T>> impl;
		std::function<T()> func;
	};

	class IHSMNoParamsFuncSensor
	{
	protected:
		IHSMNoParamsFuncSensor() = default;
	};

	template<class T>
	class HSMNoParamsFuncSensor : IHSMNoParamsFuncSensor
	{
	public:
		using ResultType = typename std::conditional<std::is_arithmetic_v<T>, T, std::string>::type;

		HSMNoParamsFuncSensor(std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<ResultType>> impl_wrapper) : impl_wrapper(impl_wrapper)
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
		std::shared_ptr<HSMNoParamsFuncSensorImplWrapper<ResultType>> impl_wrapper;
	};
}