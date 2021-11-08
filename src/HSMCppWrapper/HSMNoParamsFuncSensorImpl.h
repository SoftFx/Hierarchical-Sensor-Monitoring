#pragma once

#include "msclr/auto_gcroot.h"

#include "HSMBaseNoParamsFuncSensor.h"

using namespace HSMDataCollector::PublicInterface;
using System::Func;
using System::String;
using System::TimeSpan;
using System::Collections::Generic::List;

namespace hsm_wrapper
{
	template<class T>
	class HSMNoParamsFuncSensorImpl : public HSMBaseNoParamsFuncSensor<T>
	{
	public:
		using Type = typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type;

		void SetParamsFuncSensor(INoParamsFuncSensor<Type>^ new_sensor);
		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);

	private:
		msclr::auto_gcroot<INoParamsFuncSensor<Type>^> sensor;
	};

}
