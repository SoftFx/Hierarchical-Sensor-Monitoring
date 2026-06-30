#pragma once

// Native backend for HSMNoParamsFuncSensor. The managed build bridged the C++ callable to a managed
// Func<>^ via a delegate-wrapper + base class; natively the callable is passed straight to
// hsm::collector::FunctionSensor, so that machinery is gone. The native function sensor's period is
// fixed at creation, so the interval is cached here (RestartTimer cannot re-arm the native timer).

#include "hsm_collector/hsm_collector.hpp"

#include <chrono>

namespace hsm_wrapper
{
	template<class T>
	class HSMNoParamsFuncSensorImpl
	{
	public:
		void SetParamsFuncSensor(hsm::collector::FunctionSensor new_sensor, std::chrono::milliseconds interval);
		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);

	private:
		hsm::collector::FunctionSensor sensor;
		std::chrono::milliseconds interval{};
	};
}
