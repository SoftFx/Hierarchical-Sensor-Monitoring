#pragma once

#include <chrono>
#include <functional>

namespace hsm_wrapper
{
	template<class T, class U>
	class HSMParamsFuncSensorImpl;

	template<class T, class U>
	class HSMWRAPPER_API HSMParamsFuncSensorImplWrapper
	{
	public:
		using ResultType = T;
		using ElementType = U;

		HSMParamsFuncSensorImplWrapper(std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> impl);

		void SetFunc(std::function<T(std::list<U>)> parent_func);

		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);
		void AddValue(U value);

		T Func(const std::list<U>& values);

	private:
		std::shared_ptr<HSMParamsFuncSensorImpl<T, U>> impl;
		std::function<T(std::list<U>)> func;
	};
	
	template<class T, class U>
	class HSMParamsFuncSensor
	{
	public:
		using ResultType = typename std::conditional<std::is_arithmetic_v<T>, T, std::string>::type;
		using ElementType = typename std::conditional<std::is_arithmetic_v<U>, U, std::string>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<U>, U, const U&>::type;

		HSMParamsFuncSensor(std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper) : impl_wrapper(impl_wrapper)
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

		void AddValue(ElementParameterType value)
		{
			if constexpr (std::is_arithmetic_v<U> || std::is_same_v<U, std::string>)
				impl_wrapper->AddValue(value);
			else
				impl_wrapper->AddValue(value.ToString());
		}
	private:
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper;
	};

}