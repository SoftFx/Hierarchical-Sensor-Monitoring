#pragma once

namespace hsm_wrapper
{
	template<class T>
	class HSMRateSensorImpl;

	class IHSMRateSensor
	{
	protected:
		IHSMRateSensor() = default;
	};

	template<class T>
	class HSMWRAPPER_API HSMRateSensor : IHSMRateSensor
	{
	public:
		using ElementType = T;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMRateSensor(std::shared_ptr<HSMRateSensorImpl<T>> sensor_impl);

		void AddValue(ElementParameterType value);
	private:
		std::shared_ptr<HSMRateSensorImpl<T>> impl;
	};

	using IntRateSensor = HSMRateSensor<int>;
	using DoubleRateSensor = HSMRateSensor<double>;
}
