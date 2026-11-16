#include "agent/agent_runtime.hpp"

#include "agent/retired_port.hpp"

#ifdef _WIN32
#include "agent/apply_update.hpp"
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

    void AgentRuntime::SetServiceConfigPath(std::wstring path)
    {
        service_config_path_ = std::move(path);
    }

    bool AgentRuntime::StopRequested()
    {
        std::lock_guard<std::mutex> lock(mutex_);
        return stop_requested_;
    }

    void AgentRuntime::WaitForStopWatchingRetiredPort()
    {
#ifdef _WIN32
        // The schedule, the confirmation and the throttling are decided by NextRetiredPortStep
        // (retired_port.cpp, unit-tested); this loop runs the probes and carries out the step.
        const bool can_move = !service_config_path_.empty();
        RetiredPortWatchState state;
        std::chrono::milliseconds delay = kRetiredPortFirstCheckDelay;

        for (;;)
        {
            {
                std::unique_lock<std::mutex> lock(mutex_);
                if (cv_.wait_for(lock, delay, [this] { return stop_requested_; }))
                    return;
            }

            const RetiredPortVerdict verdict = JudgeRetiredPort(config_.port, [this](int port) {
                // A stop that arrived during the previous probe ends the check without another one.
                if (StopRequested())
                    return PortAnswer::Nothing;
                return ProbeServerPort(config_, port);
            });
            if (StopRequested())
                return;

            const RetiredPortStep step = NextRetiredPortStep(state, verdict, can_move);
            delay = step.next_delay;

            switch (step.action)
            {
            case RetiredPortAction::Wait:
                continue;
            case RetiredPortAction::ReportUnreachable:
                Log(hc::LogLevel::Error, RetiredPortUnreachableNotice(config_.port, can_move));
                continue;
            case RetiredPortAction::ReportUnrecognised:
                Log(hc::LogLevel::Error, RetiredPortUnrecognisedNotice(config_.port));
                continue;
            case RetiredPortAction::ReportManualMove:
                // A foreground run: report, do not touch the file or the service.
                Log(hc::LogLevel::Error, RetiredPortManualMoveNotice(config_.port));
                continue;
            case RetiredPortAction::Move:
                break;
            }

            // Same persist-then-restart sequence as a sensor-group directive: the collector's endpoint
            // is built once at Start, so the new port needs a fresh process, and config_ itself is
            // left alone (UpdateChecker reads it on its own thread). The file is read back and only
            // the port changes, and only while it still names the address and port that were
            // probed (MoveConfigToStandardPort).
            //
            // The directive handler (collector thread) also writes config.json from a copy of
            // config_, and nothing serializes the two writers. They cannot overlap: a directive
            // arrives in the response to a data POST, or (server.transport = session, #1660 step 5)
            // as a push on the collector session, which is a connection to the same port; and the
            // move happens only after the configured port stopped answering on two checks in a
            // row, so no POST through it, no session on it, and no directive, can be in flight.
            // Revisit this if a directive can ever arrive another way.
            std::string cfg_err;
            MoveOutcome outcome = MoveOutcome::Failed;
            try
            {
                // The narrow conversion throws for a path the active code page cannot represent.
                outcome = MoveConfigToStandardPort(std::filesystem::path(service_config_path_).string(),
                                                   config_.server_address, config_.port, cfg_err);
            }
            catch (const std::exception& ex)
            {
                cfg_err = ex.what();
            }
            if (outcome == MoveOutcome::ChangedSinceStart)
            {
                if (step.failure_notice_due)
                    Log(hc::LogLevel::Error, RetiredPortChangedSinceStartNotice(config_.port));
                continue;
            }
            if (outcome != MoveOutcome::Moved)
            {
                if (step.failure_notice_due)
                    Log(hc::LogLevel::Error,
                        RetiredPortManualMoveNotice(config_.port) +
                            " (The agent could not rewrite config.json itself: " + cfg_err + ")");
                continue;
            }

            if (!SpawnRestartService())
            {
                if (step.failure_notice_due)
                    Log(hc::LogLevel::Error,
                        "config: server.port " + std::to_string(config_.port) +
                            " no longer answers; config.json now says \"port\": 443, but the restart helper could"
                            " not be started. Restart the HSMAgent service to apply.");
                continue;
            }

            Log(hc::LogLevel::Info, RetiredPortSwitchNotice(config_.port));
            RequestStop();
            return;
        }
#else
        WaitForStop();
#endif
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

            // Handle server directives delivered via data-POST response headers (#1198), or pushed
            // on the collector session when server.transport is "session" (#1660 step 5).
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
                    const std::string& group = payload;
                    bool AgentConfig::* member = SensorGroupFlag(group);
                    if (member == nullptr)
                    {
                        Log(hc::LogLevel::Error, "directive: unknown sensor group '" + group + "'");
                        return;
                    }

                    // The server re-sends the full desired state (all six groups) on EVERY data POST,
                    // delivered as separate headers processed one-by-one. Act only when THIS group
                    // actually changes — checked against the live config_, which we update in place
                    // below so a second changed group in the SAME response accumulates instead of
                    // clobbering the first. Unchanged groups return here (no restart loop).
                    if (config_.*member == enable)
                        return;

                    // Persist a copy with this one flag flipped. config_ itself is only ever bool-mutated
                    // (below, after a successful write), never whole-struct reassigned: UpdateChecker
                    // holds config_ by reference and reads its std::string members on its own thread, so
                    // a string-reallocating assign here would be a data race / use-after-free.
                    AgentConfig updated = config_;
                    updated.*member = enable;

                    std::string cfg_err;
                    const auto cfg_path_w = DefaultConfigPath();
                    const std::string cfg_path = std::filesystem::path(cfg_path_w).string();
                    if (!WriteAgentConfig(cfg_path, updated, cfg_err))
                    {
                        // Persist failed — do NOT restart and do NOT touch config_: a restart would
                        // reload the unchanged file and the re-sent directive would just fail again.
                        Log(hc::LogLevel::Error, "directive: failed to update config.json (not restarting): " + cfg_err);
                        return;
                    }

                    // A sensor-group change only takes effect on a fresh process — the collector's
                    // sensor set is built once at Start. RequestStop() alone just stops the service:
                    // the SCM does NOT auto-restart a graceful (exit-0) stop, only a crash. So spawn the
                    // detached --restart-service helper FIRST; it outlives this process, waits for
                    // STOPPED, then StartService()s us back up (the no-swap twin of the self-update
                    // --apply-update path). If several groups change in ONE response, only the first
                    // spawns the helper and calls RequestStop — the helper re-reads the now fully
                    // updated config.json, so the rest just record their flag.
                    bool already_stopping;
                    {
                        std::lock_guard<std::mutex> lock(mutex_);
                        already_stopping = stop_requested_;
                    }
                    if (!already_stopping && !SpawnRestartService())
                    {
                        // Could not spawn the restarter: do NOT stop (that would leave the agent down)
                        // and do NOT mark config_ applied, so the server's next re-send retries the
                        // spawn. The new value is already on disk, so a manual restart also applies it.
                        Log(hc::LogLevel::Error, "directive: could not spawn restart helper; config saved, will apply on next restart");
                        return;
                    }

                    config_.*member = enable; // bool-only mutation (no string realloc -> no data race)
                    Log(hc::LogLevel::Info,
                        "directive: " + verb + ":" + group +
                            (already_stopping ? " — applies on the pending restart" : " — restarting to apply"));
                    if (!already_stopping)
                        RequestStop();
                }
            };
#endif

            hc::Collector collector(opts);

            // Install the log sink first so configuration/transport diagnostics are captured.
            if (log_)
                collector.SetLogger(log_);

            // Real server transport (#1165) + the Windows PDH/Win32 live readers (#1164). Both must be
            // installed before Start. `server.transport` picks requests or the collector session
            // (#1668); there is no fallback from one to the other.
            if (config_.UsesSession())
                collector.UseSessionTransport();
            else
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

            // TCP connection failure rate (failures/min) — an admin "velocity of failure" signal
            // (service down / port exhaustion). Windows-only; the collector rejects the call on other
            // platforms, so guard here. Lightweight single sensor, enabled by default.
#ifdef _WIN32
            collector.EnableTcpConnectionFailureRateSensor(std::chrono::milliseconds(60000));
#endif

            // Raise the built-in Total CPU sensor to Error when CPU stays high. The default catalog
            // already WARNS at EmaMean > 50%; here we additionally flip it to ERROR at the same point
            // (5-min confirmation suppresses transient bursts). Done by attaching an extra alert to the
            // existing sensor — NOT a catalog change, so the shared default/golden/managed stay put.
            // add_default_sensor is idempotent by path: AddAllComputerSensors already created Total CPU,
            // this returns that same handle; we attach before Start (rebuilds its registration) and free
            // the handle (the collector still owns the sensor). Cross-platform (Total CPU is universal).
            {
                // Build the alert BEFORE acquiring the sensor handle so a throw from the fluent builder
                // (CreateAlert/Build use ThrowIfFailed) can't leak the handle.
                const hc::Alert cpu_error_alert =
                    collector.CreateAlert(hc::AlertKind::Bar)
                        .If(hc::AlertProperty::EmaMean, hc::AlertOperation::GreaterThan, "50")
                        .AsSensorError()
                        .ThenNotify("[$product]$path CPU sustained above 50% (EMA mean $value$unit)")
                        .WithIcon(hc::AlertIcon::Error)
                        .WithConfirmationPeriod(std::chrono::minutes(5))
                        .Build();

                hsm_sensor_t* cpu_sensor = nullptr;
                if (hsm_collector_add_default_sensor(collector.handle(), HSM_DEFAULT_TOTAL_CPU, nullptr, &cpu_sensor) == HSM_RESULT_OK &&
                    cpu_sensor != nullptr)
                {
                    hsm_sensor_attach_alert(cpu_sensor, cpu_error_alert.handle());
                    hsm_sensor_release(cpu_sensor);
                }
            }

            Log(hc::LogLevel::Info,
                "HSM Agent starting: streaming to " + config_.server_address + ":" + std::to_string(config_.port));

            // A config that still names 44330/44333 (#1655): say so at every start, then watch the
            // port below. The server cannot report a connection that never reaches it. The log has
            // no warning level: the notices that describe a working or self-healed state are Info,
            // "nothing answers" is Error (it reaches the Windows Event Log).
            const bool retired_port = IsRetiredServerPort(config_.port);
            if (retired_port)
                Log(hc::LogLevel::Info, RetiredPortStartupNotice(config_.port, !service_config_path_.empty()));

            collector.Start();

            if (on_started)
                on_started();

#ifdef _WIN32
            updater.Start();
#endif

            if (retired_port)
                WaitForStopWatchingRetiredPort();
            else
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
