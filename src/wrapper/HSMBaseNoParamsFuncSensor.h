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