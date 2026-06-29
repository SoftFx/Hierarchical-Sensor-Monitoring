#pragma once

/// @file
/// @brief `--apply-update` CLI mode for hsm-agent (epic #1174).
///
/// RunApplyUpdate() is called when the agent is launched as a detached helper process with the
/// --apply-update argument. It waits for the HSMAgent service to reach STOPPED, performs the
/// binary swap dance (hsm-agent.exe → hsm-agent.old.exe, hsm-agent.new.exe → hsm-agent.exe),
/// starts the service, and validates the health gate. On failure it rolls back and restores the
/// old binary.

namespace hsm::agent
{
    /// Entry point for the --apply-update detached helper. Returns 0 on success, non-zero on any
    /// failure (rename error, start failure, health gate failure after rollback).
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
