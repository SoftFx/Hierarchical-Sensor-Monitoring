#pragma once

#include <chrono>
#include <functional>

namespace hsm_wrapper
{
	template<class T, class U, typename = void>
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
	
	template<class T, class U, typename = void>
	class HSMParamsFuncSensor;

	template<class T, class U>
	class HSMParamsFuncSensor<T, U, typename std::enable_if_t<std::is_arithmetic_v<T> && std::is_arithmetic_v<U>>>
	{
	public:
		using ResultType = T;
		using ElementType = U;

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

		void AddValue(U value)
		{
			impl_wrapper->AddValue(value);
		}
	private:
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper;
	};

	template<class T, class U>
	class HSMParamsFuncSensor<T, U, typename std::enable_if_t<!std::is_arithmetic_v<T> && std::is_arithmetic_v<U>>>
	{
	public:
		using ResultType = std::string;
		using ElementType = U;

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

		void AddValue(U value)
		{
			impl_wrapper->AddValue(value);
		}
	private:
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper;
	};

	template<class T, class U>
	class HSMParamsFuncSensor<T, U, typename std::enable_if_t<std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>>>
	{
	public:
		using ResultType = T;
		using ElementType = std::string;

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

		void AddValue(const U& value)
		{
			impl_wrapper->AddValue(value.ToString());
		}
	private:
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper;
	};

	template<class T, class U>
	class HSMParamsFuncSensor<T, U, typename std::enable_if_t<!std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>>>
	{
	public:
		using ResultType = std::string;
		using ElementType = std::string;

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

		void AddValue(const U& value)
		{
			impl_wrapper->AddValue(value.ToString());
		}
	private:
		std::shared_ptr<HSMParamsFuncSensorImplWrapper<ResultType, ElementType>> impl_wrapper;
	};

}