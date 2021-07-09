#pragma once

#include "msclr/auto_gcroot.h"

using namespace HSMDataCollector::PublicInterface;

namespace hsm_wrapper
{
	template<class T>
	struct DefaultSensorType;

	template<>
	struct DefaultSensorType<int>
	{
		using type = IDefaultValueSensorInt^;
	};

	template<>
	struct DefaultSensorType<double>
	{
		using type = IDefaultValueSensorDouble^;
	};

	template<class T>
	class HSMDefaultSensorImpl
	{
	public:
		HSMDefaultSensorImpl(typename DefaultSensorType<T>::type sensor);
		~HSMDefaultSensorImpl() = default;
		HSMDefaultSensorImpl() = delete;
		HSMDefaultSensorImpl(const HSMDefaultSensorImpl&) = delete;
		HSMDefaultSensorImpl(HSMDefaultSensorImpl&&) = delete;
		HSMDefaultSensorImpl& operator=(const HSMDefaultSensorImpl&) = delete;
		HSMDefaultSensorImpl& operator=(HSMDefaultSensorImpl&&) = delete;

		void AddValue(T value);
		void AddValue(T value, const std::string& comment);
		void AddValue(T value, HSMSensorStatus status, const std::string& comment);
	private:
		msclr::auto_gcroot<typename DefaultSensorType<T>::type> sensor;
	};
}
