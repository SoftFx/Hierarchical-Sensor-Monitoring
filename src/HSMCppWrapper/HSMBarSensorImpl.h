#pragma once

#include "msclr/auto_gcroot.h"

using System::String;

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct BarSensorType
	{
		using type = IBarSensor<T>^;
	};

	template<class T>
	class HSMBarSensorImpl
	{
	public:
		HSMBarSensorImpl(typename BarSensorType<T>::type sensor);

		void AddValue(T value);
	private:
		msclr::auto_gcroot<typename BarSensorType<T>::type> sensor;
	};
}