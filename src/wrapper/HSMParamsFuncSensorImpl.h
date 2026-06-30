#pragma once

// Native backend for HSMParamsFuncSensor over hsm::collector::ValuesFunctionSensor (int-valued).

#include "hsm_collector/hsm_collector.hpp"

#include <chrono>
#include <cstdint>
#include <type_traits>

namespace hsm_wrapper
{
	template<class T, class U>
	class HSMParamsFuncSensorImpl
	{
	public:
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<U>, U, const std::string&>::type;

		void SetParamsFuncSensor(hsm::collector::ValuesFunctionSensor new_sensor, std::chrono::milliseconds interval);
		std::chrono::milliseconds GetInterval();
		void RestartTimer(std::chrono::milliseconds time_interval);
		void AddValue(ElementParameterType value);

	private:
		hsm::collector::ValuesFunctionSensor sensor;
		std::chrono::milliseconds interval{};
	};
}
