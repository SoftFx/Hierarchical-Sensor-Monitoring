#pragma once

#include "SensorStatus.h"

namespace hsm_wrapper
{
	template<class T>
	class HSMSensorImpl;

	template<class T>
	class HSMWRAPPER_API HSMSensor
	{
	public:
		using ElementType = T;

		HSMSensor(std::shared_ptr<HSMSensorImpl<T>> sensor_impl);

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		std::shared_ptr<HSMSensorImpl<T>> impl;
	};




	using BoolSensor = HSMSensor<bool>;
	using IntSensor = HSMSensor<int>;
	using DoubleSensor = HSMSensor<double>;
	using StringSensor = HSMSensor<const std::string&>;
}