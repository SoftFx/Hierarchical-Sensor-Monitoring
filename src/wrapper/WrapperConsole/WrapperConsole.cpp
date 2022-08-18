// WrapperConsole.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>

#include "include/HSMCppWrapper.h"


using namespace hsm_wrapper;

int main()
{
    DataCollectorProxy collector("b73d5b7e-412f-4f08-89a3-b18c42a81cb7", "https://hsm.dev.soft-fx.eu/", 44330);

    collector.InitializeSystemMonitoring(true, true);

    int a;

    std::cin >> a;
}