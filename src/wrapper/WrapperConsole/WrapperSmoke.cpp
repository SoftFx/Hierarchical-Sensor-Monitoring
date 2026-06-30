// Minimal in-process smoke for the native HSMCppWrapper: drives the public ABI exactly as a consumer
// (e.g. tt-aggregator2) would, without a live server. It exercises construction, every sensor kind,
// the alert DSL, the values-function bridge, file + service-state, and start/stop. Success = no
// throw and a non-null DLL surface; it does not assert wire output (that is the conformance suite).

#include "HSMCppWrapper.h"

// Variant A: a consumer that wants to create new sensors directly against the native collector
// includes this and calls proxy.Native(). It links the wrapper DLL only — the native C ABI comes
// from the DLL's re-exports, no second copy of the collector runtime.
#include "hsm_collector/hsm_collector.hpp"

#include <chrono>
#include <iostream>
#include <list>
#include <numeric>
#include <thread>

using namespace hsm_wrapper;

int main()
{
	try
	{
		DataCollectorProxy collector("00000000-0000-0000-0000-000000000000", "127.0.0.1", 44330, "Smoke");
		collector.Initialize();

		collector.InitializeSystemMonitoring(true, true, false);
		collector.InitializeProcessMonitoring(true, true, true, false);
		collector.InitializeNetworkMonitoring(true, true, true);
		collector.InitializeCollectorMonitoring(true, true, true);
		collector.InitializeQueueDiagnostic(true, true, true, true);
		collector.InitializeProductVersion("1.2.3");
		collector.AddServiceStateMonitoring("Spooler");

		auto boolSensor = collector.CreateBoolSensor("smoke/bool", "a bool");
		auto intSensor = collector.CreateIntSensor("smoke/int", "an int");
		auto doubleSensor = collector.CreateDoubleSensor("smoke/double", "a double");
		auto stringSensor = collector.CreateStringSensor("smoke/string", "a string");
		auto intBar = collector.CreateIntBarSensor("smoke/intbar");
		auto doubleBar = collector.CreateDoubleBarSensor("smoke/doublebar");
		auto intRate = collector.CreateIntRateSensor("smoke/intrate");
		auto lastInt = collector.CreateLastValueIntSensor("smoke/lastint", 0, "last int");

		// Alert DSL: warn when the value exceeds a threshold.
		HSMInstantSensorOptions options;
		options.description = "guarded int";
		options.alerts.push_back(
			AlertsBuilder::IfValue(HSMAlertOperation::GreaterThan, 100)
				.ThenSendNotification("too big: $value")
				.AndSetSensorError()
				.Build());
		auto guarded = collector.CreateIntSensor("smoke/guarded", options);

		// Values-function bridge — the shape tt-aggregator2 uses (<int, int>): sum the window.
		auto summator = collector.CreateParamsFuncSensor<int, int>(
			"smoke/sum", "sum of window",
			[](const std::list<int>& values) { return std::accumulate(values.begin(), values.end(), 0); },
			std::chrono::seconds(60));

		// Variant A: create a NEW sensor directly on the native collector (the way new aggregator
		// sensors will be written) — same collector, same connection, no wrapper sensor type.
		hsm::collector::Collector& native = collector.Native();
		hsm::collector::SensorOptions nativeOptions;
		nativeOptions.description = "created directly via Native()";
		hsm::collector::DoubleSensor nativeSensor = native.CreateDoubleSensor("smoke/native_direct", nativeOptions);

		collector.StartAsync();

		boolSensor.AddValue(true);
		intSensor.AddValue(7);
		doubleSensor.AddValue(3.14);
		stringSensor.AddValue("hello");
		intBar.AddValue(42);
		doubleBar.AddValue(2.71);
		intRate.AddValue(5);
		lastInt.AddValue(99);
		guarded.AddValue(150);
		summator.AddValue(1);
		summator.AddValue(2);
		summator.AddValue(3);
		nativeSensor.AddValue(1.23);

		std::this_thread::sleep_for(std::chrono::milliseconds(200));
		collector.Stop();

		std::cout << "SMOKE_OK" << std::endl;
		return 0;
	}
	catch (const std::exception& ex)
	{
		std::cerr << "SMOKE_FAIL: " << ex.what() << std::endl;
		return 1;
	}
}
