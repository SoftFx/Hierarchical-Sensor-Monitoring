// Transport tests of the wrapper (#1848 part 5): the exported API of HSMCppWrapper against the
// scripted servers of the native collector's tests. A proxy started with nothing else said is on the
// collector session; UseHttpTransport() before Start puts it on HTTP with no upgrade attempted; a
// table sensor made through Native() registers and delivers on the session and is Unavailable on HTTP.
//
// Run one case by name: WrapperTransportTests <case>. With no argument every case runs.
//
// The test DLL (HSMCppWrapperTransportTest) differs from the shipped one only in that it may reach a
// plaintext server and sends its packages every 20 ms (HSMWRAPPER_TRANSPORT_TESTS). Waits poll a
// condition against a deadline; no sleep stands in for synchronization.

#include "HSMCppWrapper.h"

#include "hsm_collector/hsm_collector.hpp"
#include "hsm_collector/tables.hpp"

#include "http_capture_server.hpp"
#include "ws_test_server.hpp"

#include <chrono>
#include <functional>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace
{
	constexpr const char* kKey = "00000000-0000-0000-0000-000000000000";
	constexpr const char* kTableSubprotocol = "hsm.tables.v1";

	void Require(bool condition, const std::string& message)
	{
		if (!condition)
			throw std::runtime_error(message);
	}

	// Re-checks the condition every 20 ms until it holds or the deadline passes. Generous deadlines:
	// other heavy test runs may share the machine, and a passing case returns as soon as it holds.
	bool WaitUntil(const std::function<bool()>& condition, std::chrono::milliseconds timeout = std::chrono::seconds(30))
	{
		const auto deadline = std::chrono::steady_clock::now() + timeout;
		while (!condition())
		{
			if (std::chrono::steady_clock::now() >= deadline)
				return false;
			std::this_thread::sleep_for(std::chrono::milliseconds(20));
		}
		return true;
	}

	hsm::test::WebSocketTestServer::Options SessionServerOptions()
	{
		hsm::test::WebSocketTestServer::Options options;
		options.session = true;
		options.side_subprotocol = kTableSubprotocol;
		return options;
	}

	bool FrameIsOp(const std::string& frame, const std::string& op)
	{
		return frame.find("\"op\":\"" + op + "\"") != std::string::npos;
	}

	// Accepted request frames of `op` that contain `text`.
	size_t SessionRequests(const hsm::test::WebSocketTestServer& server, const std::string& op, const std::string& text)
	{
		size_t count = 0;
		for (const auto& request : server.SessionRequests())
		{
			if (request.status >= 200 && request.status < 300 && FrameIsOp(request.frame, op) && request.frame.find(text) != std::string::npos)
				++count;
		}
		return count;
	}

	size_t HttpRequests(const hsm::test::HttpRecordingServer& server, const std::string& path_suffix, const std::string& text)
	{
		size_t count = 0;
		for (const auto& request : server.Requests())
		{
			const bool path_matches = request.path.size() >= path_suffix.size() &&
				request.path.compare(request.path.size() - path_suffix.size(), path_suffix.size(), path_suffix) == 0;
			if (path_matches && request.body.find(text) != std::string::npos)
				++count;
		}
		return count;
	}

	size_t UpgradeRequests(const hsm::test::HttpRecordingServer& server)
	{
		size_t count = 0;
		for (const auto& request : server.Requests())
		{
			if (request.path.find("/session") != std::string::npos)
				++count;
		}
		return count;
	}

	bool AnyUpgradeRequest(const hsm::test::HttpRecordingServer& server)
	{
		return UpgradeRequests(server) != 0;
	}

	std::string StateName(hsm::collector::TableSensorState state)
	{
		switch (state)
		{
		case hsm::collector::TableSensorState::Pending: return "Pending";
		case hsm::collector::TableSensorState::Registered: return "Registered";
		case hsm::collector::TableSensorState::Refused: return "Refused";
		case hsm::collector::TableSensorState::Unavailable: return "Unavailable";
		}
		return "?";
	}

	hsm::collector::TableSensorOptions OneColumn()
	{
		hsm::collector::TableSensorOptions options;
		options.columns = { hsm::collector::TableColumn::Double("price") };
		options.description = "wrapper table";
		return options;
	}

	// ---- cases --------------------------------------------------------------------------------

	void DefaultTransportIsTheSession()
	{
		hsm::test::WebSocketTestServer server(SessionServerOptions());
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		auto sensor = proxy.CreateIntSensor("wrapper/session_int", "an int");

		proxy.Start();
		sensor.AddValue(4242);

		Require(WaitUntil([&] { return SessionRequests(server, "list", "\"Value\":4242") >= 1; }),
			"the value must arrive in a list frame of the session");
		Require(SessionRequests(server, "commands", "wrapper/session_int") >= 1, "the sensor must be registered over the session");
		Require(server.Upgrades() >= 1, "the collector must have upgraded to the session");
		Require(server.AcceptedHandshakeHead().find("/api/sensors/session") != std::string::npos,
			"the upgrade must go to the session route; head: " + server.AcceptedHandshakeHead());
		Require(server.PlainHttpRequests() == 0, "a collector on the session must send nothing over plain HTTP");

		proxy.Stop();
	}

	void UseHttpTransportSendsOverHttpWithoutUpgrade()
	{
		hsm::test::HttpRecordingServer server;
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		proxy.UseHttpTransport();
		auto sensor = proxy.CreateIntSensor("wrapper/http_int", "an int");

		proxy.Start();
		sensor.AddValue(4343);

		Require(WaitUntil([&] { return HttpRequests(server, "/list", "\"Value\":4343") >= 1; }),
			"the value must arrive at the HTTP list route");
		Require(HttpRequests(server, "/commands", "wrapper/http_int") >= 1, "the sensor must be registered over HTTP");

		proxy.Stop();
		Require(!AnyUpgradeRequest(server), "a collector on HTTP must never ask for the session");
	}

	void UseHttpTransportAfterStartThrowsAndKeepsTheSession()
	{
		hsm::test::WebSocketTestServer server(SessionServerOptions());
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		auto sensor = proxy.CreateIntSensor("wrapper/late_int", "an int");

		proxy.Start();
		bool threw = false;
		try
		{
			proxy.UseHttpTransport();
		}
		catch (const hsm::collector::Error& error)
		{
			threw = std::string(error.what()).find("before Start") != std::string::npos;
		}
		Require(threw, "UseHttpTransport on a running collector must throw hsm::collector::Error naming Start");

		sensor.AddValue(4444);
		Require(WaitUntil([&] { return SessionRequests(server, "list", "\"Value\":4444") >= 1; }),
			"the collector must stay on the session after the refused call");
		Require(server.PlainHttpRequests() == 0, "nothing may go over plain HTTP");
		proxy.Stop();

		// After Stop the call is accepted again, as the native collector's transport calls are.
		proxy.UseHttpTransport();
	}

	// StartAsync returns before its thread has entered the native Start, so the collector's status
	// is still Stopped then: the refusal must come from the request itself, whatever that thread did.
	void StartAsyncThenUseHttpTransportThrowsAndKeepsTheSession()
	{
		hsm::test::WebSocketTestServer server(SessionServerOptions());
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		auto sensor = proxy.CreateIntSensor("wrapper/async_int", "an int");

		proxy.StartAsync();
		bool threw = false;
		try
		{
			proxy.UseHttpTransport();
		}
		catch (const hsm::collector::Error& error)
		{
			threw = std::string(error.what()).find("before Start") != std::string::npos;
		}
		Require(threw, "UseHttpTransport right after StartAsync must throw hsm::collector::Error naming Start");

		Require(WaitUntil([&] { return server.Upgrades() >= 1; }), "the started collector must open the session");
		sensor.AddValue(4545);
		Require(WaitUntil([&] { return SessionRequests(server, "list", "\"Value\":4545") >= 1; }),
			"the collector must be on the session after the refused call");
		Require(server.PlainHttpRequests() == 0, "nothing may go over plain HTTP");
		proxy.Stop();
	}

	// A StopAsync in flight is waited for: UseHttpTransport after it is accepted, and the next Start
	// sends over HTTP with no upgrade.
	void StopAsyncThenUseHttpTransportWaitsForTheStop()
	{
		hsm::test::HttpRecordingServer server;
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		auto sensor = proxy.CreateIntSensor("wrapper/stopasync_int", "an int");

		proxy.StartAsync();
		proxy.StopAsync();
		proxy.UseHttpTransport(); // must not throw: it waits for the stop
		const size_t upgrades = UpgradeRequests(server);

		proxy.Start();
		sensor.AddValue(4646);
		Require(WaitUntil([&] { return HttpRequests(server, "/list", "\"Value\":4646") >= 1; }),
			"after the stop and the HTTP choice the value must arrive at the HTTP list route");
		proxy.Stop();
		Require(UpgradeRequests(server) == upgrades, "no upgrade may be asked for after the HTTP choice");
	}

	// The explicit choice made after a Stop holds for the next Start.
	void UseHttpTransportAfterStopSendsOverHttp()
	{
		hsm::test::HttpRecordingServer server;
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		auto sensor = proxy.CreateIntSensor("wrapper/afterstop_int", "an int");

		proxy.Start(); // on the session: this server has none, so nothing is delivered
		proxy.Stop();
		proxy.UseHttpTransport();
		const size_t upgrades = UpgradeRequests(server);

		proxy.Start();
		sensor.AddValue(4747);
		Require(WaitUntil([&] { return HttpRequests(server, "/list", "\"Value\":4747") >= 1; }),
			"after UseHttpTransport the value must arrive at the HTTP list route");
		proxy.Stop();
		Require(UpgradeRequests(server) == upgrades, "no upgrade may be asked for after the HTTP choice");
	}

	// Start, Stop, Start: an explicit HTTP choice is kept, not replaced by the session at each Start.
	void UseHttpTransportIsKeptAcrossRestarts()
	{
		hsm::test::HttpRecordingServer server;
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		proxy.UseHttpTransport();
		auto sensor = proxy.CreateIntSensor("wrapper/restart_int", "an int");

		proxy.Start();
		sensor.AddValue(4848);
		Require(WaitUntil([&] { return HttpRequests(server, "/list", "\"Value\":4848") >= 1; }), "the first run must send over HTTP");
		proxy.Stop();

		proxy.Start();
		sensor.AddValue(4949);
		Require(WaitUntil([&] { return HttpRequests(server, "/list", "\"Value\":4949") >= 1; }), "the second run must send over HTTP too");
		proxy.Stop();
		Require(!AnyUpgradeRequest(server), "a collector on HTTP must never ask for the session");
	}

	void TableSensorDeliversOnTheDefaultTransport()
	{
		hsm::test::WebSocketTestServer server(SessionServerOptions());
		// Every block is stored; no table is refused by name.
		server.EnableTableScript([](const std::string&) { return std::vector<std::string>{}; });

		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		hsm::collector::TableOptions table_options;
		table_options.tick_period = std::chrono::seconds(1); // the smallest the collector takes
		proxy.Native().SetTableOptions(table_options);

		// Made before Start: the proxy has chosen the session already, so the sensor is not Unavailable.
		auto table = proxy.Native().CreateTableSensor("wrapper/table", OneColumn());
		const auto before = table.Stats();
		Require(before.state == hsm::collector::TableSensorState::Pending,
			"a table sensor made before Start on the default transport must be Pending, not " + StateName(before.state) + " (" + before.reason + ")");

		proxy.Start();
		Require(WaitUntil([&] { return table.Stats().state == hsm::collector::TableSensorState::Registered; }),
			"the table sensor must register over the session; state " + StateName(table.Stats().state) + " (" + table.Stats().reason + ")");

		Require(table.AddRow(std::chrono::system_clock::now(), { 1.25 }), "the row must be taken");
		Require(WaitUntil([&] {
			for (const auto& frame : server.TableFrames())
			{
				if (frame.stored)
					return true;
			}
			return false;
		}),
			"a block must be stored over the table connection");
		Require(server.TableConnections() >= 1, "the table connection must be open");
		Require(server.PlainHttpRequests() == 0, "nothing may go over plain HTTP");

		const auto stats = table.Stats();
		Require(stats.rows_accepted == 1 && stats.rows_dropped == 0, "one row accepted, none dropped");
		proxy.Stop();
	}

	void TableSensorIsUnavailableOnHttp()
	{
		hsm::test::HttpRecordingServer server;
		hsm_wrapper::DataCollectorProxy proxy(kKey, "http://127.0.0.1", server.Port(), "WrapperTests");
		proxy.UseHttpTransport();

		auto table = proxy.Native().CreateTableSensor("wrapper/http_table", OneColumn());
		auto stats = table.Stats();
		Require(stats.state == hsm::collector::TableSensorState::Unavailable, "a table sensor on HTTP must be Unavailable, not " + StateName(stats.state));
		Require(stats.reason.find("session") != std::string::npos, "the reason must name the session: " + stats.reason);

		proxy.Start();
		stats = table.Stats();
		Require(stats.state == hsm::collector::TableSensorState::Unavailable, "still Unavailable after Start, not " + StateName(stats.state));
		Require(stats.reason.find("session") != std::string::npos, "the reason after Start must name the session: " + stats.reason);
		Require(!table.AddRow(std::chrono::system_clock::now(), { 2.5 }), "the row must be dropped");
		Require(table.Stats().rows_dropped == 1, "the dropped row must be counted");
		proxy.Stop();
		Require(!AnyUpgradeRequest(server), "a collector on HTTP must never ask for the session");
	}

	const std::map<std::string, void (*)()>& Cases()
	{
		static const std::map<std::string, void (*)()> cases = {
			{ "wrapper_default_transport_is_the_session", &DefaultTransportIsTheSession },
			{ "wrapper_use_http_transport_sends_over_http_without_upgrade", &UseHttpTransportSendsOverHttpWithoutUpgrade },
			{ "wrapper_use_http_transport_after_start_throws_and_keeps_the_session", &UseHttpTransportAfterStartThrowsAndKeepsTheSession },
			{ "wrapper_start_async_then_use_http_transport_throws_and_keeps_the_session", &StartAsyncThenUseHttpTransportThrowsAndKeepsTheSession },
			{ "wrapper_stop_async_then_use_http_transport_waits_for_the_stop", &StopAsyncThenUseHttpTransportWaitsForTheStop },
			{ "wrapper_use_http_transport_after_stop_sends_over_http", &UseHttpTransportAfterStopSendsOverHttp },
			{ "wrapper_use_http_transport_is_kept_across_restarts", &UseHttpTransportIsKeptAcrossRestarts },
			{ "wrapper_table_sensor_delivers_on_the_default_transport", &TableSensorDeliversOnTheDefaultTransport },
			{ "wrapper_table_sensor_is_unavailable_on_http", &TableSensorIsUnavailableOnHttp },
		};
		return cases;
	}
}

int main(int argc, char** argv)
{
	std::vector<std::string> names;
	if (argc > 1)
		names.emplace_back(argv[1]);
	else
		for (const auto& entry : Cases())
			names.push_back(entry.first);

	int failed = 0;
	for (const auto& name : names)
	{
		const auto found = Cases().find(name);
		if (found == Cases().end())
		{
			std::cerr << "UNKNOWN CASE " << name << std::endl;
			return 2;
		}
		try
		{
			found->second();
			std::cout << "PASS " << name << std::endl;
		}
		catch (const std::exception& ex)
		{
			++failed;
			std::cerr << "FAIL " << name << ": " << ex.what() << std::endl;
		}
	}
	return failed == 0 ? 0 : 1;
}
