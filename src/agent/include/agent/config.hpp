#pragma once

/// @file
/// @brief HSM Agent configuration: the `config.json` schema, its parser, and the file loader.
///
/// The config is the ONLY per-install input. The signed `hsm-agent.exe` is byte-identical across all
/// downloads; everything machine/product-specific (server address, access key, identity, sensor
/// groups) lives here so the PE is never patched (Authenticode invariant, epic #1167).
///
/// This translation unit is platform-agnostic (no collector / Win32 includes) so the parser can be
/// unit-tested on every CI lane. `"auto"` computer-name resolution and the mapping onto
/// CollectorOptions happen in the Windows runtime (agent_runtime.cpp).

#include <string>

namespace hsm::agent
{
    /// The fully-parsed agent configuration. Every field has a sensible default so a minimal
    /// `{ "server": { "address": "...", "accessKey": "..." } }` already monitors the whole host.
    struct AgentConfig
    {
        // server.*
        std::string server_address; ///< required, non-blank
        int port = 44330;           ///< HSM Sensor API port
        std::string access_key;     ///< required, non-blank
        bool allow_untrusted_certificate = false;

        // identity.*
        std::string computer_name = "auto"; ///< "auto"/empty → resolved to the machine name at runtime
        std::string module = "HSM Agent";

        // sensors.* group gates (default: monitor everything host-level)
        bool sensors_computer = true;
        bool sensors_system = true;
        bool sensors_disk = true;
        bool sensors_network = true;
        bool sensors_module = true;
        bool sensors_process = false; ///< per-process sensors are opt-in

        // periods.*
        int collect_period_ms = 0; ///< 0 → collector default (15000)

        // module-sensor product version
        std::string product_version = "1.0.0.0";

        /// True once `computer_name` should be replaced by the resolved machine name.
        bool ComputerNameIsAuto() const;
    };

    /// Parse `config.json` text into `out`. Returns false and fills `error` with a human-readable
    /// message on malformed JSON, a wrong-typed field, or a blank required field (address/accessKey).
    /// On success `out` is fully populated (defaults applied for absent fields).
    bool ParseAgentConfig(const std::string& json_text, AgentConfig& out, std::string& error);

    /// Read `path` and parse it. Returns false and fills `error` if the file cannot be opened or the
    /// content fails `ParseAgentConfig`.
    bool LoadAgentConfig(const std::string& path, AgentConfig& out, std::string& error);
} // namespace hsm::agent
