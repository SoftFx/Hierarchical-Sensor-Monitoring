#pragma once

#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	class HSMSensorImpl
	{
	public:
		using ElementType = typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMSensorImpl(IInstantValueSensor<ElementType>^ sensor);

		void AddValue(ElementParameterType value);
		void AddValue(ElementParameterType value, const std::string& comment);
		void AddValue(ElementParameterType value, HSMSensorStatus status, const std::string& comment);
	private:
		msclr::auto_gcroot<IInstantValueSensor<ElementType>^> sensor;
	};
}