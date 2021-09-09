#pragma once

#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct SensorType
	{
		using type = IInstantValueSensor<T>^;
	};

	template<>
	struct SensorType<const std::string&>
	{
		using type = IInstantValueSensor<String^>^;
	};

	template<class T>
	class HSMSensorImpl
	{
	public:
		HSMSensorImpl(typename SensorType<T>::type sensor);

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		msclr::auto_gcroot<typename SensorType<T>::type> sensor;
	};
}