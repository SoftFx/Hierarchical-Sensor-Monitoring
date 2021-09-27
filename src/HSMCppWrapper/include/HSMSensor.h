#pragma once

#include "SensorStatus.h"

namespace hsm_wrapper
{
	template<class T>
	class HSMSensorImpl;

	class IHSMSensor
	{
	protected:
		IHSMSensor() = default;
	};

	template<class T>
	class HSMWRAPPER_API HSMSensor : IHSMSensor
	{
	public:
		using ElementType = T;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMSensor(std::shared_ptr<HSMSensorImpl<T>> sensor_impl);

		void AddValue(ElementParameterType value);
		void AddValue(ElementParameterType value, const std::string& comment);
		void AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment);
	private:
		std::shared_ptr<HSMSensorImpl<T>> impl;
	};




	using BoolSensor = HSMSensor<bool>;
	using IntSensor = HSMSensor<int>;
	using DoubleSensor = HSMSensor<double>;
	using StringSensor = HSMSensor<std::string>;
}