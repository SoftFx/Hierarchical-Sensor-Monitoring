// WrapperConsole.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <iostream>

#include "../include/HSMCppWrapper.h"
#include <thread>

using namespace hsm_wrapper;

int main()
{
    
    DataCollectorProxy collector("bf9fc183-64bf-4c54-89e5-f129e34854d8", "https://hsm.dev.soft-fx.eu", 44330, "console");

	collector.Initialize("", true);
	//collector.InitializeAllDisksMonitoring();
// 	collector.InitializeOsMonitoring();
// 	collector.InitializeNetworkMonitoring();
// 	collector.InitializeSystemMonitoring();
// 	collector.InitializeCollectorMonitoring();
// 	collector.InitializeProcessMonitoring();
// 	collector.InitializeQueueDiagnostic();

#define USE_ASYNC 1

#ifdef USE_ASYNC
	std::cout << " before startasync\n";
	collector.StartAsync();
	std::cout << " after startasync\n";
#else
	std::cout << " before start\n";
	collector.Start();
	std::cout << " after start\n";
#endif
	int a;

	auto intsensor = collector.CreateIntSensor("TestInt");

	auto intratesensor = collector.CreateIntRateSensor("TestRateInt", 1000);

	intsensor.AddValue(1);

	for (int i = 0; i < 50; i++)
	{
		intratesensor.AddValue(i);
		std::cout << ".";
		std::this_thread::sleep_for(std::chrono::milliseconds(100));
	}

	std::cout << "OK.\n";

	std::this_thread::sleep_for(std::chrono::seconds(1));

	intsensor.AddValue(0);
	std::cout << "Done.\n";

#ifdef USE_ASYNC
    collector.StopAsync();
#else
	collector.Stop();
#endif

}