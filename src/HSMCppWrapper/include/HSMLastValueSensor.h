#pragma once

#include "SensorStatus.h"

namespace hsm_wrapper
{
	template<class T>
	class HSMLastValueSensorImpl;

	class IHSMLastValueSensor
	{
	protected:
		IHSMLastValueSensor() = default;
	};

	template<class T>
	class HSMWRAPPER_API HSMLastValueSensor : IHSMLastValueSensor
	{
	public:
		using ElementType = T;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMLastValueSensor(std::shared_ptr<HSMLastValueSensorImpl<T>> sensor_impl);

		void AddValue(ElementParameterType value);
		void AddValue(ElementParameterType value, const std::string& comment);
		void AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment);
	private:
		std::shared_ptr<HSMLastValueSensorImpl<T>> impl;
	};




	using BoolLastValueSensor = HSMLastValueSensor<bool>;
	using IntLastValueSensor = HSMLastValueSensor<int>;
	using DoubleLastValueSensor = HSMLastValueSensor<double>;
	using StringLastValueSensor = HSMLastValueSensor<std::string>;
}