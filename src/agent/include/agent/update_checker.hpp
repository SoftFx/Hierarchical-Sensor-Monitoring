#pragma once

/// @file
/// @brief Background agent self-update checker (epic #1174).
///
/// UpdateChecker runs on a dedicated thread. It periodically polls GET /api/agent/version on the
/// server. When a newer build is available AND the server's global switch (updateEnabled) is true,
/// it downloads the exe, verifies its SHA-256, stages it as hsm-agent.new.exe, spawns itself with
/// --apply-update, then requests a graceful stop of the current service run.

#include "agent/config.hpp"

#include <functional>
#include <string>

namespace hsm::agent
{
    /// Encapsulates the background update-check loop. Lifetime: construct after the runtime starts,
    /// destroy (Stop()) before the runtime exits.
    class UpdateChecker
    {
    public:
        /// Log sink: void(int level, const std::string& message). Level values match
        /// hsm::collector::LogLevel (Info=2, Warn=3, Error=4).
        using LogFn = std::function<void(int, const std::string&)>;

        /// `request_stop` is called once when an update is ready to apply (the checker has staged
        /// hsm-agent.new.exe and spawned --apply-update). The runtime must then begin its shutdown
        /// sequence so the service can be restarted by the apply-update helper.
        UpdateChecker(const AgentConfig& config, LogFn log, std::function<void()> request_stop);
        ~UpdateChecker();

        UpdateChecker(const UpdateChecker&) = delete;
        UpdateChecker& operator=(const UpdateChecker&) = delete;

        /// Start the background polling thread.
        void Start();

        /// Signal the thread to exit and block until it does.
        void Stop();

    private:
        void Run();
        bool CheckAndUpdate();

        const AgentConfig& config_;
        LogFn log_;
        std::function<void()> request_stop_;

        void* thread_handle_ = nullptr; // HANDLE, void* to avoid Win32 headers in this header
        void* stop_event_ = nullptr;    // HANDLE
    };
} // namespace hsm::agent
