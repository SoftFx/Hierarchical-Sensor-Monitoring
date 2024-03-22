#pragma once


#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
 	template<class T>
 	struct RateSensorType
 	{
 		using Type = IMonitoringRateSensor^;
 	};

	template<class T>
	class HSMRateSensorImpl
	{
	public:
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMRateSensorImpl(typename RateSensorType<T>::Type sensor);

		void AddValue(ElementParameterType value);
	private:
		msclr::auto_gcroot<typename RateSensorType<T>::Type> sensor;
	};
}