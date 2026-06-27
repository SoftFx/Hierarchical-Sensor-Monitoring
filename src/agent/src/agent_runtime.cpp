#include "agent/agent_runtime.hpp"

#ifdef _WIN32
#include "agent/config.hpp"
#include "agent/paths.hpp"
#include "agent/update_checker.hpp"
#endif

#include <chrono>
#include <exception>
#include <filesystem>
#include <utility>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace hsm::agent
{
    namespace hc = hsm::collector;

    AgentRuntime::AgentRuntime(AgentConfig config, LogFn log)
        : config_(std::move(config)), log_(std::move(log))
    {
    }

    void AgentRuntime::Log(hc::LogLevel level, const std::string& message) const
    {
        if (log_)
            log_(level, message);
    }

    std::string AgentRuntime::ResolveComputerName() const
    {
        if (!config_.ComputerNameIsAuto())
            return config_.computer_name;

#ifdef _WIN32
        // Physical DNS host name matches what the .NET collector reports for the local machine.
        wchar_t buffer[256];
        DWORD size = static_cast<DWORD>(std::size(buffer));
        if (GetComputerNameExW(ComputerNamePhysicalDnsHostname, buffer, &size) && size > 0)
        {
            const int needed = WideCharToMultiByte(CP_UTF8, 0, buffer, static_cast<int>(size), nullptr, 0, nullptr, nullptr);
            if (needed > 0)
            {
                std::string utf8(static_cast<std::size_t>(needed), '\0');
                WideCharToMultiByte(CP_UTF8, 0, buffer, static_cast<int>(size), utf8.data(), needed, nullptr, nullptr);
                return utf8;
            }
        }
#endif
        // Fall back to an empty name (the collector omits an empty computer segment).
        return std::string{};
    }

    hc::CollectorOptions AgentRuntime::BuildOptions() const
    {
        hc::CollectorOptions options;
        options.access_key = config_.access_key;
        options.server_address = config_.server_address;
        options.port = config_.port;
        options.module = config_.module;
        options.computer_name = ResolveComputerName();
        options.allow_untrusted_server_certificate = config_.allow_untrusted_certificate;
        if (config_.collect_period_ms > 0)
            options.package_collect_period_ms = config_.collect_period_ms;
        return options;
    }

    void AgentRuntime::WaitForStop()
    {
        std::unique_lock<std::mutex> lock(mutex_);
        cv_.wait(lock, [this] { return stop_requested_; });
    }

    void AgentRuntime::RequestStop()
    {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_requested_ = true;
        }
        cv_.notify_all();
    }

    int AgentRuntime::Run(const std::function<void()>& on_started)
    {
        try
        {
#ifdef _WIN32
            // If a previous self-update succeeded, clean up the old binary now that the new
            // service is confirmed running (this is the first Run() call after the swap).
            const std::wstring old_exe = OldExePath();
            if (GetFileAttributesW(old_exe.c_str()) != INVALID_FILE_ATTRIBUTES)
                DeleteFileW(old_exe.c_str());
#endif

#ifdef _WIN32
            // Create the UpdateChecker before the collector so the directive callback can reference
            // it by address. The thread is not started until updater.Start() below.
            UpdateChecker updater(
                config_,
                [this](int level, const std::string& msg) {
                    Log(static_cast<hc::LogLevel>(level), msg);
                },
                [this] { RequestStop(); });
#endif

            hc::CollectorOptions opts = BuildOptions();

#ifdef _WIN32
            // X-Agent-Version: server uses it to decide whether to emit update-available directives.
            opts.extra_request_headers.push_back({"X-Agent-Version", HSM_AGENT_VERSION});

            // Handle server directives delivered via data-POST response headers (#1198).
            opts.on_server_directive = [this, &updater](const std::string& verb, const std::string& payload)
            {
                if (verb == "update-available")
                {
                    // Server knows there is a newer binary; wake the UpdateChecker immediately
                    // instead of waiting for the next scheduled poll.
                    updater.TriggerCheck();
                }
                else if (verb == "sensor-disable" || verb == "sensor-enable")
                {
                    const bool enable = (verb == "sensor-enable");
                    AgentConfig updated = config_;
                    const std::string& group = payload;
                    bool* flag = nullptr;
                    if      (group == "computer") flag = &updated.sensors_computer;
                    else if (group == "system")   flag = &updated.sensors_system;
                    else if (group == "disk")     flag = &updated.sensors_disk;
                    else if (group == "network")  flag = &updated.sensors_network;
                    else if (group == "module")   flag = &updated.sensors_module;
                    else if (group == "process")  flag = &updated.sensors_process;
                    else
                    {
                        Log(hc::LogLevel::Error, "directive: unknown sensor group '" + group + "'");
                        return;
                    }

                    // The server re-sends this directive on EVERY data POST while the group stays
                    // toggled, so act only when it actually changes our state — otherwise we would
                    // rewrite config.json and restart on every response (an endless restart loop).
                    if (*flag == enable)
                        return;

                    *flag = enable;
                    // Do NOT copy `updated` back into config_: UpdateChecker holds config_ by reference
                    // and reads its std::string members (server_address/access_key) on its own thread,
                    // so a whole-struct reassign here is a data race / use-after-free. The persisted
                    // file is the source of truth; the restart below reloads it.

                    // Persist the change so it survives the restart we are about to trigger.
                    std::string cfg_err;
                    const auto cfg_path_w = DefaultConfigPath();
                    const std::string cfg_path = std::filesystem::path(cfg_path_w).string();
                    if (!WriteAgentConfig(cfg_path, updated, cfg_err))
                    {
                        // Persist failed — do NOT restart: a restart would reload the unchanged config
                        // and the re-sent directive would just fail to write and restart again (a loop).
                        Log(hc::LogLevel::Error, "directive: failed to update config.json (not restarting): " + cfg_err);
                        return;
                    }

                    Log(hc::LogLevel::Info, "directive: " + verb + ":" + group + " — restarting to apply");
                    RequestStop();
                }
            };
#endif

            hc::Collector collector(opts);

            // Install the log sink first so configuration/transport diagnostics are captured.
            if (log_)
                collector.SetLogger(log_);

            // Real server transport (#1165) + the Windows PDH/Win32 live readers (#1164). Both must be
            // installed before Start.
            collector.UseHttpTransport();
            collector.InstallWindowsMetricSources();

            // Standard host catalog, gated by the config groups. `computer` is the umbrella bundle
            // (CPU/RAM/disks/network/OS info); the finer system/disk/network flags select subsets only
            // when the umbrella is disabled. Module + process are independent.
            if (config_.sensors_computer)
            {
                collector.AddAllComputerSensors();
            }
            else
            {
                if (config_.sensors_system)
                    collector.AddSystemMonitoringSensors();
                if (config_.sensors_disk)
                    collector.AddDiskMonitoringSensors();
                if (config_.sensors_network)
                    collector.AddAllNetworkSensors();
            }

            if (config_.sensors_process)
            {
                // Register the process sensors individually and SKIP the managed-only
                // "ThreadPool thread count" — it is a .NET CLR concept (no managed thread pool
                // exists in a native C++ process, so the native source can only report 0).
                // The collector library still exposes it via AddProcessMonitoringSensors for
                // managed consumers, so parity/conformance is untouched; only this agent omits it.
                collector.AddDefaultSensor(hc::DefaultSensor::ProcessCpu);
                collector.AddDefaultSensor(hc::DefaultSensor::ProcessMemory);
                collector.AddDefaultSensor(hc::DefaultSensor::ProcessThreadCount);
            }

            // NOTE: do NOT override ".module/Collector version" with the agent's version — the
            // collector is a separate product (HSMDataCollector line, reported via
            // HSM_COLLECTOR_PRODUCT_VERSION) from this agent (HSM_AGENT_VERSION). They carry
            // distinct version numbers on purpose: ".module/Collector version" = collector product,
            // ".module/Version" = this agent.
            if (config_.sensors_module)
                // The agent IS the monitored application, so report its own build version
                // (HSM_AGENT_VERSION) as ".module/Version" — it then tracks every self-update,
                // instead of the static config placeholder. Falls back to config only if a custom
                // productVersion was explicitly set to a non-default value.
                collector.AddAllModuleSensors(
                    (config_.product_version.empty() || config_.product_version == "1.0.0.0")
                        ? HSM_AGENT_VERSION
                        : config_.product_version);

            // Periodic "top processes by CPU" sensors (#1175/#1179). Windows-only; the collector
            // rejects the call on non-Windows, so guard here to avoid a fatal throw on Linux builds.
#ifdef _WIN32
            if (config_.top_cpu_enabled)
                collector.EnableTopCpuSensors(
                    config_.top_cpu_count,
                    config_.top_cpu_min_percent,
                    std::chrono::milliseconds(config_.top_cpu_period_ms));
#endif

            Log(hc::LogLevel::Info,
                "HSM Agent starting: streaming to " + config_.server_address + ":" + std::to_string(config_.port));

            collector.Start();

            if (on_started)
                on_started();

#ifdef _WIN32
            updater.Start();
#endif

            WaitForStop();

#ifdef _WIN32
            updater.Stop();
#endif

            Log(hc::LogLevel::Info, "HSM Agent stopping...");
            collector.Stop(); // bounded graceful drain (capped internally)
            Log(hc::LogLevel::Info, "HSM Agent stopped.");
            return 0;
        }
        catch (const std::exception& ex)
        {
            Log(hc::LogLevel::Error, std::string{ "HSM Agent fatal error: " } + ex.what());
            return 1;
        }
    }
} // namespace hsm::agent
