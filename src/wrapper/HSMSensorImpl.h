#pragma once

// Native backend for HSMSensor<T> — wraps an hsm::collector instant value sensor instead of the
// managed IInstantValueSensor<T>^ the C++/CLI build used. The public header (include/HSMSensor.h)
// is unchanged: this only replaces the hidden pimpl.

#include "HSMEnums.h"

#include "hsm_collector/hsm_collector.hpp"

#include <memory>
#include <string>
#include <type_traits>

namespace hsm_wrapper
{
	namespace detail
	{
		// Maps the wrapper element type to the concrete hsm::collector instant sensor type.
		template<class T>
		struct NativeInstantSensor;
		template<>
		struct NativeInstantSensor<bool>
		{
			using type = hsm::collector::BoolSensor;
		};
		template<>
		struct NativeInstantSensor<int>
		{
			using type = hsm::collector::IntSensor;
		};
		template<>
		struct NativeInstantSensor<double>
		{
			using type = hsm::collector::DoubleSensor;
		};
		template<>
		struct NativeInstantSensor<std::string>
		{
			using type = hsm::collector::StringSensor;
		};
	}

	template<class T>
	class HSMSensorImpl
	{
	public:
		using NativeSensor = typename detail::NativeInstantSensor<T>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		explicit HSMSensorImpl(NativeSensor sensor) : sensor(std::move(sensor))
		{
		}

		void AddValue(ElementParameterType value);
		void AddValue(ElementParameterType value, const std::string& comment);
		void AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment);

	private:
		NativeSensor sensor;
	};
}
