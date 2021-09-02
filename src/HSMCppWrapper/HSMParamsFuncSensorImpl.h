#pragma once

#include "msclr/auto_gcroot.h"

#include "HSMBaseFuncSensor.h"

using namespace HSMDataCollector::PublicInterface;
using System::Func;
using System::String;
using System::TimeSpan;
using System::Collections::Generic::List;

namespace hsm_wrapper
{
	template<class T, class U>
	class HSMParamsFuncSensorImpl<T, U, typename std::enable_if_t<std::is_arithmetic_v<T> && std::is_arithmetic_v<U>>> : public HSMBaseFuncSensor<T, U>
	{
	public:
		using ResultType = T;
		using ElementType = U;

		HSMParamsFuncSensorImpl()
		{
		}

		~HSMParamsFuncSensorImpl() = default;
		HSMParamsFuncSensorImpl(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl(HSMParamsFuncSensorImpl&&) = default;
		HSMParamsFuncSensorImpl& operator=(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl& operator=(HSMParamsFuncSensorImpl&&) = default;

		void SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor)
		{
			sensor = new_sensor;
		}

		std::chrono::milliseconds GetInterval()
		{
			return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
		}

		void RestartTimer(std::chrono::milliseconds time_interval)
		{
			sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
		}

		void AddValue(U value)
		{
			sensor->AddValue(value);
		}
	private:
		msclr::auto_gcroot<IParamsFuncSensor<ResultType, ElementType>^> sensor;
	};

	template<class T, class U>
	class HSMParamsFuncSensorImpl<T, U, typename std::enable_if_t<!std::is_arithmetic_v<T> && std::is_arithmetic_v<U>>> : public HSMBaseFuncSensor<T, U>
	{
	public:
		using ResultType = String^;
		using ElementType = U;

		HSMParamsFuncSensorImpl()
		{
		}

		~HSMParamsFuncSensorImpl() = default;
		HSMParamsFuncSensorImpl(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl(HSMParamsFuncSensorImpl&&) = default;
		HSMParamsFuncSensorImpl& operator=(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl& operator=(HSMParamsFuncSensorImpl&&) = default;

		void SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor)
		{
			sensor = new_sensor;
		}

		std::chrono::milliseconds GetInterval()
		{
			return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
		}

		void RestartTimer(std::chrono::milliseconds time_interval)
		{
			sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
		}

		void AddValue(U value)
		{
			sensor->AddValue(value);
		}
	private:
		msclr::auto_gcroot<IParamsFuncSensor<ResultType, ElementType>^> sensor;
	};

	template<class T, class U>
	class HSMParamsFuncSensorImpl<T, U, typename std::enable_if_t<std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>>> : public HSMBaseFuncSensor<T, U>
	{
	public:
		using ResultType = T;
		using ElementType = String^;

		HSMParamsFuncSensorImpl()
		{
		}

		~HSMParamsFuncSensorImpl() = default;
		HSMParamsFuncSensorImpl(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl(HSMParamsFuncSensorImpl&&) = default;
		HSMParamsFuncSensorImpl& operator=(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl& operator=(HSMParamsFuncSensorImpl&&) = default;

		void SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor)
		{
			sensor = new_sensor;
		}

		std::chrono::milliseconds GetInterval()
		{
			return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
		}

		void RestartTimer(std::chrono::milliseconds time_interval)
		{
			sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
		}

		void AddValue(const std::string& value)
		{
			sensor->AddValue(gcnew String(value.c_str()));
		}
	private:
		msclr::auto_gcroot<IParamsFuncSensor<ResultType, ElementType>^> sensor;
	};

	template<class T, class U>
	class HSMParamsFuncSensorImpl<T, U, typename std::enable_if_t<!std::is_arithmetic_v<T> && !std::is_arithmetic_v<U>>> : public HSMBaseFuncSensor<T, U>
	{
	public:
		using ResultType = String^;
		using ElementType = String^;

		HSMParamsFuncSensorImpl()
		{
		}

		~HSMParamsFuncSensorImpl() = default;
		HSMParamsFuncSensorImpl(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl(HSMParamsFuncSensorImpl&&) = default;
		HSMParamsFuncSensorImpl& operator=(const HSMParamsFuncSensorImpl&) = default;
		HSMParamsFuncSensorImpl& operator=(HSMParamsFuncSensorImpl&&) = default;

		void SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor)
		{
			sensor = new_sensor;
		}

		std::chrono::milliseconds GetInterval()
		{
			return std::chrono::milliseconds(static_cast<int>(sensor->GetInterval().TotalMilliseconds));
		}

		void RestartTimer(std::chrono::milliseconds time_interval)
		{
			sensor->RestartTimer(TimeSpan::FromMilliseconds(time_interval.count()));
		}

		void AddValue(const std::string& value)
		{
			sensor->AddValue(gcnew String(value.c_str()));
		}
	private:
		msclr::auto_gcroot<IParamsFuncSensor<ResultType, ElementType>^> sensor;
	};

}