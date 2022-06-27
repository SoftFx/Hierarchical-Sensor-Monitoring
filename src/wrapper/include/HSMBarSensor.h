#pragma once

namespace hsm_wrapper
{
	template<class T>
	class HSMBarSensorImpl;

	class IHSMBarSensor
	{
	protected:
		IHSMBarSensor() = default;
	};

	template<class T>
	class HSMWRAPPER_API HSMBarSensor : IHSMBarSensor
	{
	public:
		using ElementType = T;
		using ElementParameterType = typename std::conditional<std::is_arithmetic_v<T>, T, const T&>::type;

		HSMBarSensor(std::shared_ptr<HSMBarSensorImpl<T>> sensor_impl);

		void AddValue(ElementParameterType value);
	private:
		std::shared_ptr<HSMBarSensorImpl<T>> impl;
	};




	using IntBarSensor = HSMBarSensor<int>;
	using DoubleBarSensor = HSMBarSensor<double>;
}