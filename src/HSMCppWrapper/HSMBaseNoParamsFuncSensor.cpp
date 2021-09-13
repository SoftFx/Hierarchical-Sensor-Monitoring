#include "pch.h"

#include "HSMBaseNoParamsFuncSensor.h"

using namespace std;
using namespace hsm_wrapper;


template<class T>
HSMBaseNoParamsFuncSensor<T>::HSMBaseNoParamsFuncSensor()
{
	delegate_wrapper = gcnew NoParamsFuncDelegateWrapper<T>(this);
}

template<class T>
NoParamsFuncDelegateWrapper<T>^ HSMBaseNoParamsFuncSensor<T>::GetDelegateWrapper()
{
	return delegate_wrapper.get();
}

template<class T>
void HSMBaseNoParamsFuncSensor<T>::SetFunc(std::function<T()> function)
{
	func = function;
}

template<class T>
T HSMBaseNoParamsFuncSensor<T>::Func()
{
	return func();
}




#define InstantiateTemplates(X)\
template HSMBaseNoParamsFuncSensor<X>;


InstantiateTemplates(int)
InstantiateTemplates(double)
InstantiateTemplates(bool)
InstantiateTemplates(string)