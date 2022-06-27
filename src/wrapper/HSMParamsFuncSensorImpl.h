#pragma once

#include "msclr/auto_gcroot.h"

#include "HSMBaseParamsFuncSensor.h"

using namespace HSMDataCollector::PublicInterface;
using System::Func;
using System::String;
using System::TimeSpan;
using System::Collections::Generic::List;

namespace hsm_wrapper
{
	template<class T, class U>
	class HSMParamsFuncSensorImpl : public HSMBaseParamsFuncSensor<T, U>
	{
	public:
		using ResultType = typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type;
		using ElementType = typename std::conditional<std::is_arithmetic_v<U>, U, String^>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<U>, U, const std::string&>::type;

		void SetParamsFuncSensor(IParamsFuncSensor<ResultType, ElementType>^ new_sensor);
		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);
		void AddValue(ElementParameterType value);

	private:
		msclr::auto_gcroot<IParamsFuncSensor<ResultType, ElementType>^> sensor;
	};
	
}