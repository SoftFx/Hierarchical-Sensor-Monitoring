#pragma once

#include "msclr/auto_gcroot.h"

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct SensorType;

	template<>
	struct SensorType<bool>
	{
		using type = IBoolSensor^;
	};

	template<>
	struct SensorType<int>
	{
		using type = IIntSensor^;
	};

	template<>
	struct SensorType<double>
	{
		using type = IDoubleSensor^;
	};

	template<>
	struct SensorType<const std::string&>
	{
		using type = IStringSensor^;
	};

	template<class T>
	class HSMSensorImpl
	{
	public:
		HSMSensorImpl(typename SensorType<T>::type sensor);
		~HSMSensorImpl() = default;
		HSMSensorImpl() = delete;
		HSMSensorImpl(const HSMSensorImpl&) = delete;
		HSMSensorImpl(HSMSensorImpl&&) = delete;
		HSMSensorImpl& operator=(const HSMSensorImpl&) = delete;
		HSMSensorImpl& operator=(HSMSensorImpl&&) = delete;

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		msclr::auto_gcroot<typename SensorType<T>::type> sensor;
	};
}