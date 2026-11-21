#pragma once

/// @file
/// @brief `--apply-update` CLI mode for hsm-agent (epic #1174, #1812).
///
/// RunApplyUpdate() is called when the agent is launched as a detached helper process with the
/// --apply-update argument. It waits for the HSMAgent service to reach STOPPED, performs the
/// binary swap dance (hsm-agent.exe → hsm-agent.old.exe, hsm-agent.new.exe → hsm-agent.exe) in the
/// directory of its own exe (the helper is spawned from the installed exe), starts the service, and
/// validates the health gate. Whatever fails after the service stopped, it rolls back and starts the
/// service again on the old binary: a failed update never leaves the service stopped.

#include "agent/paths.hpp"

#include <filesystem>
#include <functional>
#include <string>

namespace hsm::agent
{
    /// Whether the service's configured binary is a given exe.
    enum class ServiceBinaryMatch
    {
        Same,      ///< the service runs this very file
        Different, ///< the service runs another file
        Unknown,   ///< could not tell (SCM query or file access failed)
    };

    /// The few service-control calls the swap needs: the seam that lets the swap, the health gate and
    /// the rollback run against a temp directory in tests, without a real SCM.
    class ServiceControl
    {
    public:
        virtual ~ServiceControl() = default;

        /// Wait until the service has stopped (and its process exited). False on timeout.
        virtual bool WaitUntilStopped(unsigned timeout_ms) = 0;

        /// Ask the SCM to start the service. True when it started or was already running.
        virtual bool Start() = 0;

        /// Wait until the service reports RUNNING. False on timeout or when it stopped instead.
        virtual bool WaitUntilRunning(unsigned timeout_ms) = 0;

        /// Ask the service to stop and wait up to `timeout_ms` for it to stop; a process still hung in
        /// START_PENDING then is ended, one in STOP_PENDING gets more time. True when it stopped.
        virtual bool Stop(unsigned timeout_ms) = 0;

        /// Whether the service's configured binary is the file at `exe_path`.
        virtual ServiceBinaryMatch RunsExe(const std::wstring& exe_path) = 0;
    };

    /// The exe path of a service command line (QUERY_SERVICE_CONFIG lpBinaryPathName): the quoted
    /// part, or for an unquoted one everything up to and including the first ".exe" that ends a word.
    /// Environment variables are not expanded here. Pure.
    std::wstring ServiceCommandExePath(const std::wstring& command);

    /// True when both paths name the same existing file (same volume and file index).
    bool SameFile(const std::wstring& a, const std::wstring& b);

    /// Log sink of the helper: `error` selects the Event Log type.
    using ApplyLogFn = std::function<void(bool error, const std::string& message)>;

    /// The swap itself, on `files` and `service`. Returns 0 when the new binary runs and passed the
    /// health gate (the attempt record at `state_path` is then removed); non-zero otherwise. Swaps only
    /// when the service runs `files.exe`; when it runs another file the helper was not spawned by the
    /// service, and it leaves the service alone. Every failure after the service stopped ends with the
    /// old binary back in place and the service started again (several start attempts); a service
    /// that never stopped is left running, and a new binary that cannot be stopped keeps running
    /// until the next service start (logged).
    int ApplyStagedUpdate(const UpdateFiles& files, ServiceControl& service, const std::filesystem::path& state_path,
                          const ApplyLogFn& log);

    /// Entry point for the --apply-update detached helper: ApplyStagedUpdate on the files next to its
    /// own exe, the real HSMAgent service and `UpdateStatePath()`.
    int RunApplyUpdate();

    /// Entry point for the --restart-service detached helper. Waits up to 60 s for the HSMAgent
    /// service to reach STOPPED, then StartService()s it again. Unlike --apply-update there is NO
    /// binary swap — it just bounces the service so an on-disk config change (a sensor-group
    /// enable/disable directive, #1198) takes effect, since the collector's sensor set is built once
    /// at Start. Returns 0 on success, non-zero on any failure.
    int RunRestartService();

    /// Spawn THIS exe as a detached `--restart-service` helper. The running service calls this right
    /// before RequestStop()ing: the helper outlives the service process, waits for it to stop, then
    /// starts it back up — a graceful (exit-0) stop is NOT auto-restarted by the SCM. Returns false
    /// if the child process could not be created.
    bool SpawnRestartService();
} // namespace hsm::agent
