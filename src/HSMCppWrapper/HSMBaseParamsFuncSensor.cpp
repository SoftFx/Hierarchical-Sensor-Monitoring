#include "pch.h"

#include "HSMBaseParamsFuncSensor.h"

using namespace std;
using namespace hsm_wrapper;


template<class T, class U>
HSMBaseParamsFuncSensor<T, U>::HSMBaseParamsFuncSensor()
{
	delegate_wrapper = gcnew ParamsFuncDelegateWrapper<T, U>(this);
}

template<class T, class U>
ParamsFuncDelegateWrapper<T, U>^ HSMBaseParamsFuncSensor<T, U>::GetDelegateWrapper()
{
	return delegate_wrapper.get();
}

template<class T, class U>
void HSMBaseParamsFuncSensor<T, U>::SetFunc(std::function<T(std::list<U>)> function)
{
	func = function;
}

template<class T, class U>
T HSMBaseParamsFuncSensor<T, U>::Func(const std::list<U>& values)
{
	return func(values);
}




#define InstantiateTemplates(X, Y)\
template HSMBaseParamsFuncSensor<X, Y>;


InstantiateTemplates(int, int)
InstantiateTemplates(int, double)
InstantiateTemplates(int, bool)
InstantiateTemplates(int, string)

InstantiateTemplates(double, int)
InstantiateTemplates(double, double)
InstantiateTemplates(double, bool)
InstantiateTemplates(double, string)

InstantiateTemplates(bool, int)
InstantiateTemplates(bool, double)
InstantiateTemplates(bool, bool)
InstantiateTemplates(bool, string)

InstantiateTemplates(string, int)
InstantiateTemplates(string, double)
InstantiateTemplates(string, bool)
InstantiateTemplates(string, string)