#pragma once

#include "msclr/auto_gcroot.h"
#include "msclr/marshal_cppstd.h"

using System::Func;
using System::String;
using System::Collections::Generic::List;

namespace hsm_wrapper
{

	template<class T>
	class HSMBaseNoParamsFuncSensor;

	template<class T>
	ref class NoParamsFuncDelegateWrapper
	{
	public:
		NoParamsFuncDelegateWrapper(HSMBaseNoParamsFuncSensor<T>* base) : base(base)
		{
		}

		typename std::conditional<std::is_arithmetic_v<T>, T, String^>::type
			Call()
		{
			if constexpr (std::is_arithmetic_v<T>)
				return base->Func();
			else
				return gcnew String(base->Func().c_str());
		}

		/*
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
		*/

	private:
		HSMBaseNoParamsFuncSensor<T>* base;
	};

	template<class T>
	class HSMBaseNoParamsFuncSensor
	{
	public:
		NoParamsFuncDelegateWrapper<T>^ GetDelegateWrapper();
		void SetFunc(std::function<T()> function);
		T Func();

	protected:
		HSMBaseNoParamsFuncSensor();

	private:
		std::function<T()> func;
		msclr::auto_gcroot<NoParamsFuncDelegateWrapper<T>^> delegate_wrapper;
	};

}