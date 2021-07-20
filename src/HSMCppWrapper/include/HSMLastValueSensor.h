#pragma once

#include "SensorStatus.h"

namespace hsm_wrapper
{
	template<class T>
	class HSMLastValueSensorImpl;

	template<class T>
	class HSMWRAPPER_API HSMLastValueSensor
	{
	public:
		using type = T;

		HSMLastValueSensor(std::shared_ptr<HSMLastValueSensorImpl<T>> sensor_impl);
		HSMLastValueSensor(HSMLastValueSensor&& sensor);
		~HSMLastValueSensor() = default;
		HSMLastValueSensor() = delete;
		HSMLastValueSensor(const HSMLastValueSensor&) = delete;
		HSMLastValueSensor& operator=(const HSMLastValueSensor&) = delete;
		HSMLastValueSensor& operator=(HSMLastValueSensor&& sensor) = delete;

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		std::shared_ptr<HSMLastValueSensorImpl<T>> impl;
	};




	using BoolLastValueSensor = HSMLastValueSensor<bool>;
	using IntLastValueSensor = HSMLastValueSensor<int>;
	using DoubleLastValueSensor = HSMLastValueSensor<double>;
	using StringLastValueSensor = HSMLastValueSensor<const std::string&>;
}