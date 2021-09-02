#include "pch.h"

#include "HSMBaseFuncSensor.h"

using namespace std;
using namespace hsm_wrapper;


template<class T, class U>
HSMBaseFuncSensor<T, U>::HSMBaseFuncSensor()
{
	delegate_wrapper = gcnew DelegateWrapper<T, U>(this);
}

template<class T, class U>
DelegateWrapper<T, U>^ HSMBaseFuncSensor<T, U>::GetDelegateWrapper()
{
	return delegate_wrapper.get();
}

template<class T, class U>
void HSMBaseFuncSensor<T, U>::SetFunc(std::function<T(std::list<U>)> function)
{
	func = function;
}

template<class T, class U>
T HSMBaseFuncSensor<T, U>::Func(const std::list<U>& values)
{
	return func(values);
}




template HSMBaseFuncSensor<int, int>;
template HSMBaseFuncSensor<int, double>;
template HSMBaseFuncSensor<int, bool>;
template HSMBaseFuncSensor<int, string>;

template HSMBaseFuncSensor<double, int>;
template HSMBaseFuncSensor<double, double>;
template HSMBaseFuncSensor<double, bool>;
template HSMBaseFuncSensor<double, string>;

template HSMBaseFuncSensor<bool, int>;
template HSMBaseFuncSensor<bool, double>;
template HSMBaseFuncSensor<bool, bool>;
template HSMBaseFuncSensor<bool, string>;

template HSMBaseFuncSensor<string, int>;
template HSMBaseFuncSensor<string, double>;
template HSMBaseFuncSensor<string, bool>;
template HSMBaseFuncSensor<string, string>;
