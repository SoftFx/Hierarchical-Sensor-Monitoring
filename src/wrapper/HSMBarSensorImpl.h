#pragma once

#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct BarSensorType
	{
		using Type = IBarSensor<T>^;
	};

	template<class T>
	class HSMBarSensorImpl
	{
	public:
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMBarSensorImpl(typename BarSensorType<T>::Type sensor);

		void AddValue(ElementParameterType value);
	private:
		msclr::auto_gcroot<typename BarSensorType<T>::Type> sensor;
	};
}