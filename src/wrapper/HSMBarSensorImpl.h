#pragma once

// Native backend for HSMBarSensor<T> — wraps an hsm::collector bar sensor instead of IBarSensor<T>^.

#include "hsm_collector/hsm_collector.hpp"

#include <memory>
#include <type_traits>

namespace hsm_wrapper
{
	namespace detail
	{
		template<class T>
		struct NativeBarSensor;
		template<>
		struct NativeBarSensor<int>
		{
			using type = hsm::collector::IntBarSensor;
		};
		template<>
		struct NativeBarSensor<double>
		{
			using type = hsm::collector::DoubleBarSensor;
		};
	}

	template<class T>
	class HSMBarSensorImpl
	{
	public:
		using NativeSensor = typename detail::NativeBarSensor<T>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		explicit HSMBarSensorImpl(NativeSensor sensor) : sensor(std::move(sensor))
		{
		}

		void AddValue(ElementParameterType value);

	private:
		NativeSensor sensor;
	};
}
