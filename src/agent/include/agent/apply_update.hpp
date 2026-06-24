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
} // namespace hsm::agent
