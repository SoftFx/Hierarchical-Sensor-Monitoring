#pragma once

// Native backend for HSMLastValueSensor<T>. The native CreateLastValue*Sensor factories return the
// SAME concrete instant sensor types (Bool/Int/Double/StringSensor) — "last value" is a registration
// property, not a distinct handle type — so this reuses detail::NativeInstantSensor from HSMSensorImpl.

#include "HSMEnums.h"
#include "HSMSensorImpl.h"

#include <string>
#include <type_traits>

namespace hsm_wrapper
{
	template<class T>
	class HSMLastValueSensorImpl
	{
	public:
		using NativeSensor = typename detail::NativeInstantSensor<T>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		explicit HSMLastValueSensorImpl(NativeSensor sensor) : sensor(std::move(sensor))
		{
		}

		void AddValue(ElementParameterType value);
		void AddValue(ElementParameterType value, const std::string& comment);
		void AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment);

	private:
		NativeSensor sensor;
	};
}
