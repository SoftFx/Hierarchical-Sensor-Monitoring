// HSM Agent unit tests. Name-dispatched like the collector driver: `hsm_agent_tests <test_name>`
// exits 0 on pass, non-zero on failure. Portable (config parser only) so every CI lane can run it.

#include "agent/config.hpp"

#include <cstring>
#include <functional>
#include <iostream>
#include <map>
#include <string>

namespace
{
    int g_failures = 0;

    void Check(bool condition, const std::string& message)
    {
        if (!condition)
        {
            std::cerr << "  FAIL: " << message << '\n';
            ++g_failures;
        }
    }

    template <typename A, typename B>
    void CheckEq(const A& actual, const B& expected, const std::string& message)
    {
        if (!(actual == expected))
        {
            std::cerr << "  FAIL: " << message << " (got '" << actual << "', expected '" << expected << "')\n";
            ++g_failures;
        }
    }

    using hsm::agent::AgentConfig;
    using hsm::agent::ParseAgentConfig;

    AgentConfig ParseOk(const std::string& json)
    {
        AgentConfig config;
        std::string error;
        if (!ParseAgentConfig(json, config, error))
        {
            std::cerr << "  FAIL: expected parse to succeed, got error: " << error << '\n';
            ++g_failures;
        }
        return config;
    }

    void ExpectReject(const std::string& json, const std::string& why)
    {
        AgentConfig config;
        std::string error;
        const bool ok = ParseAgentConfig(json, config, error);
        if (ok)
        {
            std::cerr << "  FAIL: expected rejection (" << why << ") but parse succeeded\n";
            ++g_failures;
        }
        else
        {
            Check(!error.empty(), "rejection should carry a non-empty error message");
        }
    }

    // --- Tests --------------------------------------------------------------------------------

    void MinimalConfigAppliesDefaults()
    {
        const auto config = ParseOk(R"({ "server": { "address": "https://hsm.example.com", "accessKey": "abc123" } })");
        CheckEq(config.server_address, std::string{ "https://hsm.example.com" }, "address");
        CheckEq(config.access_key, std::string{ "abc123" }, "accessKey");
        CheckEq(config.port, 44330, "default port");
        CheckEq(config.module, std::string{ "HSM Agent" }, "default module");
        CheckEq(config.product_version, std::string{ "1.0.0.0" }, "default version");
        Check(config.ComputerNameIsAuto(), "computer name defaults to auto");
        Check(config.sensors_computer && config.sensors_system && config.sensors_disk && config.sensors_network && config.sensors_module,
              "all host groups default on");
        Check(!config.sensors_process, "process sensors default off");
        CheckEq(config.collect_period_ms, 0, "default collect period (collector default)");
        Check(!config.allow_untrusted_certificate, "untrusted cert defaults off");
    }

    void FullConfigMapsEveryField()
    {
        const auto config = ParseOk(R"({
            "server": { "address": "https://h:1", "port": 9999, "accessKey": "k", "allowUntrustedCertificate": true },
            "identity": { "computerName": "BUILD01", "module": "Custom Module" },
            "sensors": { "computer": false, "system": true, "disk": false, "network": true, "module": false, "process": true },
            "periods": { "collectMs": 5000 },
            "productVersion": "2.3.4.5",
            "update": { "enabled": false, "checkPeriodHours": 48 }
        })");
        CheckEq(config.server_address, std::string{ "https://h:1" }, "address");
        CheckEq(config.port, 9999, "port");
        Check(config.allow_untrusted_certificate, "untrusted cert on");
        CheckEq(config.computer_name, std::string{ "BUILD01" }, "computer name");
        Check(!config.ComputerNameIsAuto(), "explicit computer name is not auto");
        CheckEq(config.module, std::string{ "Custom Module" }, "module");
        Check(!config.sensors_computer, "computer off");
        Check(config.sensors_system, "system on");
        Check(!config.sensors_disk, "disk off");
        Check(config.sensors_network, "network on");
        Check(!config.sensors_module, "module off");
        Check(config.sensors_process, "process on");
        CheckEq(config.collect_period_ms, 5000, "collect period");
        CheckEq(config.product_version, std::string{ "2.3.4.5" }, "version");
        Check(!config.update_enabled, "update disabled");
        CheckEq(config.update_check_period_hours, 48, "update period");
    }

    void BlankAccessKeyIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "" } })", "blank key");
        ExpectReject(R"({ "server": { "address": "https://h" } })", "missing key");
    }

    void MissingAddressIsRejected()
    {
        ExpectReject(R"({ "server": { "accessKey": "k" } })", "missing address");
        ExpectReject(R"({ "server": { "address": "", "accessKey": "k" } })", "blank address");
    }

    void BadPortIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 0 } })", "port 0");
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 70000 } })", "port too high");
        // Out of 32-bit range — must be rejected, not cast (casting out-of-range double to int is UB).
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 1e10 } })", "port overflow");
        // Fractional value must be rejected, not silently truncated to 1.
        ExpectReject(R"({ "server": { "address": "https://h", "accessKey": "k", "port": 1.5 } })", "fractional port");
    }

    void MalformedJsonIsRejected()
    {
        ExpectReject(R"({ "server": { "address": )", "truncated");
        ExpectReject(R"(not json at all)", "garbage");
        ExpectReject(R"([])", "array root");
    }

    void WrongTypedFieldIsRejected()
    {
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k", "port": "nope" } })", "string port");
        ExpectReject(R"({ "server": { "address": 5, "accessKey": "k" } })", "numeric address");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "sensors": { "disk": 1 } })", "numeric bool");
    }

    void UnknownFieldsAreIgnored()
    {
        const auto config = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k", "futureFlag": 42 },
            "somethingNew": { "nested": [1, 2, 3] }
        })");
        CheckEq(config.server_address, std::string{ "h" }, "address survives unknown siblings");
    }

    void StringEscapesDecode()
    {
        const auto config = ParseOk(R"({ "server": { "address": "a\tbé", "accessKey": "k" } })");
        CheckEq(config.server_address, std::string{ "a\tb\xc3\xa9" }, "tab + unicode escape decode to UTF-8");
    }

    void TopCpuConfigParsesAndDefaults()
    {
        // Absent topCpu → feature off, sane defaults.
        const auto def = ParseOk(R"({ "server": { "address": "h", "accessKey": "k" } })");
        Check(!def.top_cpu_enabled, "topCpu off by default");
        CheckEq(def.top_cpu_period_ms, 60000, "default period");
        CheckEq(def.top_cpu_count, 10, "default count");

        const auto cfg = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k" },
            "topCpu": { "enabled": true, "periodMs": 30000, "minPercent": 2.5, "count": 5 }
        })");
        Check(cfg.top_cpu_enabled, "enabled");
        CheckEq(cfg.top_cpu_period_ms, 30000, "periodMs");
        CheckEq(cfg.top_cpu_count, 5, "count");
        Check(cfg.top_cpu_min_percent > 2.49 && cfg.top_cpu_min_percent < 2.51, "minPercent");
    }

    void TopCpuRejectsBadConfig()
    {
        // Bad values only matter when the feature is on.
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "periodMs": 0 } })", "zero period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "count": 0 } })", "zero count");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": true, "minPercent": -1 } })", "negative minPercent");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "topCpu": { "enabled": "yes" } })", "non-bool enabled");
    }

    void UpdateConfigParsesAndDefaults()
    {
        // Absent update section → enabled=true, 24 h default.
        const auto def = ParseOk(R"({ "server": { "address": "h", "accessKey": "k" } })");
        Check(def.update_enabled, "update enabled by default");
        CheckEq(def.update_check_period_hours, 24, "default check period");

        // Explicit values round-trip.
        const auto cfg = ParseOk(R"({
            "server": { "address": "h", "accessKey": "k" },
            "update": { "enabled": false, "checkPeriodHours": 12 }
        })");
        Check(!cfg.update_enabled, "disabled");
        CheckEq(cfg.update_check_period_hours, 12, "check period");
    }

    void UpdateConfigRejectsBadPeriod()
    {
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": 0 } })", "zero period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": -1 } })", "negative period");
        // 1194 h * 3600000 overflows DWORD — must be rejected.
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": { "checkPeriodHours": 9000 } })", "overflow period");
        ExpectReject(R"({ "server": { "address": "h", "accessKey": "k" }, "update": "on" })", "non-object update");
    }

    const std::map<std::string, std::function<void()>>& Tests()
    {
        static const std::map<std::string, std::function<void()>> tests = {
            { "agent_config_minimal_applies_defaults", MinimalConfigAppliesDefaults },
            { "agent_config_full_maps_every_field", FullConfigMapsEveryField },
            { "agent_config_rejects_blank_access_key", BlankAccessKeyIsRejected },
            { "agent_config_rejects_missing_address", MissingAddressIsRejected },
            { "agent_config_rejects_bad_port", BadPortIsRejected },
            { "agent_config_rejects_malformed_json", MalformedJsonIsRejected },
            { "agent_config_rejects_wrong_typed_field", WrongTypedFieldIsRejected },
            { "agent_config_ignores_unknown_fields", UnknownFieldsAreIgnored },
            { "agent_config_decodes_string_escapes", StringEscapesDecode },
            { "agent_topcpu_config_parses_and_defaults", TopCpuConfigParsesAndDefaults },
            { "agent_topcpu_rejects_bad_config", TopCpuRejectsBadConfig },
            { "agent_update_config_parses_and_defaults", UpdateConfigParsesAndDefaults },
            { "agent_update_config_rejects_bad_period", UpdateConfigRejectsBadPeriod },
        };
        return tests;
    }
} // namespace

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        std::cerr << "usage: hsm_agent_tests <test_name>\n";
        for (const auto& test : Tests())
            std::cerr << "  " << test.first << '\n';
        return 2;
    }

    const std::string name = argv[1];
    const auto& tests = Tests();
    const auto it = tests.find(name);
    if (it == tests.end())
    {
        std::cerr << "unknown test: " << name << '\n';
        return 2;
    }

    it->second();
    if (g_failures != 0)
    {
        std::cerr << name << ": " << g_failures << " check(s) failed\n";
        return 1;
    }
    std::cout << name << ": ok\n";
    return 0;
}
