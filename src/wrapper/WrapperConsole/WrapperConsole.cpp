// WrapperConsole.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>

#include "../include/HSMCppWrapper.h"


using namespace hsm_wrapper;

int main()
{
    
    DataCollectorProxy collector("bf9fc183-64bf-4c54-89e5-f129e34854d8", "https://hsm.dev.soft-fx.eu", 44330, "Feeder");

    collector.Initialize("", true);

    collector.Start();

    int a;

    std::cin >> a;

    collector.Stop();
}