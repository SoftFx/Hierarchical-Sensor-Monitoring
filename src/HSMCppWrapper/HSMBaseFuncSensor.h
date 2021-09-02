#pragma once

#include "msclr/auto_gcroot.h"

using System::Func;
using System::String;
using System::Collections::Generic::List;

namespace hsm_wrapper
{

	template<class T, class U>
	class HSMBaseFuncSensor;

	template<class T, class U>
	ref class DelegateWrapper
	{
	public:
		DelegateWrapper(HSMBaseFuncSensor<T, U>* base) : base(base)
		{
		}

		template<class X = T, class Y = U>
		typename std::enable_if_t<std::is_arithmetic_v<X> && std::is_arithmetic_v<Y>, X>
			Call(List<Y>^ values)
		{
			std::list<U> converted_values;
			for each(U value in values)
			{
				converted_values.push_back(value);
			}
			return base->Func(converted_values);
		}

		template<class X = T, class Y = U>
		typename std::enable_if_t<std::is_same_v<X, std::string> && std::is_arithmetic_v<Y>, String^>
			Call(List<Y>^ values)
		{
			std::list<U> converted_values;
			for each(U value in values)
			{
				converted_values.push_back(value);
			}
			return gcnew String(base->Func(converted_values).c_str());
		}

		template<class X = T, class Y = U>
		typename std::enable_if_t<std::is_arithmetic_v<X> && std::is_same_v<Y, std::string>, X>
			Call(List<String^>^ values)
		{
			std::list<std::string> converted_values;
			for each(String^ value in values)
			{
				converted_values.push_back(move(msclr::interop::marshal_as<std::string>(value)));
			}
			return base->Func(converted_values);
		}

		template<class X = T, class Y = U>
		typename std::enable_if_t<std::is_same_v<X, std::string> && std::is_same_v<Y, std::string>, String^>
			Call(List<String^>^ values)
		{
			std::list<std::string> converted_values;
			for each(String^ value in values)
			{
				converted_values.push_back(move(msclr::interop::marshal_as<std::string>(value)));
			}
			return gcnew String(base->Func(converted_values).c_str());
		}

	private:
		HSMBaseFuncSensor<T, U>* base;
	};

	template<class T, class U>
	class HSMBaseFuncSensor
	{
	public:
		DelegateWrapper<T, U>^ GetDelegateWrapper();
		void SetFunc(std::function<T(std::list<U>)> function);
		T Func(const std::list<U>& values);

	protected:
		HSMBaseFuncSensor();

		virtual ~HSMBaseFuncSensor() = default;
		HSMBaseFuncSensor(const HSMBaseFuncSensor&) = default;
		HSMBaseFuncSensor(HSMBaseFuncSensor&&) = default;
		HSMBaseFuncSensor& operator=(const HSMBaseFuncSensor&) = default;
		HSMBaseFuncSensor& operator=(HSMBaseFuncSensor&&) = default;

	private:
		std::function<T(std::list<U>)> func;
		msclr::auto_gcroot<DelegateWrapper<T, U>^> delegate_wrapper;
	};

}