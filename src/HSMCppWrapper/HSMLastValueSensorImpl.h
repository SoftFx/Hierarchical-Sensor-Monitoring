#pragma once

#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct DefaultSensorType
	{
		using type = ILastValueSensor<T>^;
	};

	template<>
	struct DefaultSensorType<const std::string&>
	{
		using type = ILastValueSensor<String^>^;
	};

	template<class T>
	class HSMLastValueSensorImpl
	{
	public:
		HSMLastValueSensorImpl(typename DefaultSensorType<T>::type sensor);
		~HSMLastValueSensorImpl() = default;
		HSMLastValueSensorImpl() = delete;
		HSMLastValueSensorImpl(const HSMLastValueSensorImpl&) = delete;
		HSMLastValueSensorImpl(HSMLastValueSensorImpl&&) = delete;
		HSMLastValueSensorImpl& operator=(const HSMLastValueSensorImpl&) = delete;
		HSMLastValueSensorImpl& operator=(HSMLastValueSensorImpl&&) = delete;

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		msclr::auto_gcroot<typename DefaultSensorType<T>::type> sensor;
	};
}
