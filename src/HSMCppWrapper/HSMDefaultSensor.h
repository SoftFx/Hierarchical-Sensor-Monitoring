#pragma once

#include "SensorStatus.h"

namespace hsm_wrapper
{
	template<class T>
	class HSMDefaultSensorImpl;

	template<class T>
	class HSMWRAPPER_API HSMDefaultSensor
	{
	public:
		HSMDefaultSensor(std::shared_ptr<HSMDefaultSensorImpl<T>> sensor_impl);
		HSMDefaultSensor(HSMDefaultSensor&& sensor);
		~HSMDefaultSensor() = default;
		HSMDefaultSensor() = delete;
		HSMDefaultSensor(const HSMDefaultSensor&) = delete;
		HSMDefaultSensor& operator=(const HSMDefaultSensor&) = delete;
		HSMDefaultSensor& operator=(HSMDefaultSensor&& sensor) = delete;

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		std::shared_ptr<HSMDefaultSensorImpl<T>> impl;
	};




	using DefaultDoubleSensor = HSMDefaultSensor<double>;
	using DefaultIntSensor = HSMDefaultSensor<int>;
}