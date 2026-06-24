#pragma once

/// @file
/// @brief AgentRuntime: the long-running host that builds, starts, and gracefully stops the
/// native collector from an AgentConfig. The productionized form of examples/windows-monitor.

#include "agent/config.hpp"

#include <hsm_collector/hsm_collector.hpp>

#include <condition_variable>
#include <functional>
#include <mutex>
#include <string>

namespace hsm::agent
{
    /// Owns the collector for the life of one run. `Run()` blocks on the calling (worker) thread
    /// until `RequestStop()` is called from another thread (the service control handler or a Ctrl-C
    /// handler), then performs a bounded graceful stop. Not copyable.
    class AgentRuntime
    {
    public:
        using LogFn = std::function<void(hsm::collector::LogLevel, const std::string&)>;

        AgentRuntime(AgentConfig config, LogFn log);

        AgentRuntime(const AgentRuntime&) = delete;
        AgentRuntime& operator=(const AgentRuntime&) = delete;

        /// Configure → Start → block until stop requested → bounded Stop. Returns 0 on a clean run,
        /// non-zero on a fatal error (bad options, transport unavailable). A non-zero return lets the
        /// service report a non-zero exit so SCM failure-actions restart it. `on_started` (if set) is
        /// invoked once, right after the collector starts successfully — the service uses it to
        /// transition START_PENDING → RUNNING only after the pipeline is actually live.
        int Run(const std::function<void()>& on_started = {});

        /// Signal the blocked `Run()` to begin shutdown. Thread-safe; idempotent.
        void RequestStop();

    private:
        hsm::collector::CollectorOptions BuildOptions() const;
        std::string ResolveComputerName() const;
        void Log(hsm::collector::LogLevel level, const std::string& message) const;
        void WaitForStop();

        /// Periodic "top processes by CPU" loop (issue #1175). Runs on its own thread between Start and
        /// stop when `topCpu.enabled`; posts the busiest exe names to `Top CPU processes/<exe>` Double
        /// sensors each `topCpu.periodMs`. Returns promptly when stop is requested.
        void RunTopCpuLoop(hsm::collector::Collector& collector);

        AgentConfig config_;
        LogFn log_;

        std::mutex mutex_;
        std::condition_variable cv_;
        bool stop_requested_ = false;
    };
} // namespace hsm::agent
