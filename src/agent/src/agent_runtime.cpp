#include "agent/agent_runtime.hpp"

#include <chrono>
#include <exception>
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
            hc::Collector collector(BuildOptions());

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
                collector.AddProcessMonitoringSensors();

            if (config_.sensors_module)
                collector.AddAllModuleSensors(config_.product_version);

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

            WaitForStop();

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
