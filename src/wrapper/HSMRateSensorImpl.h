#pragma once

// Native backend for HSMRateSensor<T> — both IntRateSensor and DoubleRateSensor wrap the single
// hsm::collector::RateSensor (rate is double-valued natively, matching the managed collector).

#include "hsm_collector/hsm_collector.hpp"

#include <memory>
#include <type_traits>

namespace hsm_wrapper
{
	template<class T>
	class HSMRateSensorImpl
	{
	public:
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		explicit HSMRateSensorImpl(hsm::collector::RateSensor sensor) : sensor(std::move(sensor))
		{
		}

		void AddValue(ElementParameterType value);

	private:
		hsm::collector::RateSensor sensor;
	};
}
