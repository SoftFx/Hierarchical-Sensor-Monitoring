#pragma once

/// @file
/// @brief SCM service entry. Hands the process to the service control dispatcher and runs the
/// AgentRuntime on a worker thread until the SCM stops it.

#include <string>

namespace hsm::agent
{
    /// RunService's exit code when the process was not started by the SCM (a bare run by hand).
    inline constexpr int kNotStartedByScmExitCode = 2;

    /// Connect to the service control dispatcher and run the HSMAgent service, loading config from
    /// `config_path`. Returns the process exit code. If the process was not launched by the SCM,
    /// returns kNotStartedByScmExitCode and prints guidance (use --console instead) to stderr.
    int RunService(const std::wstring& config_path);

    /// #1559: after a bare run that RunService rejected with kNotStartedByScmExitCode, explain in a
    /// message box what the exe is and how to install it — but only on an interactive launch (non-zero
    /// session, visible window station, stdin + stdout on a real console). Scripted, CI and SCM
    /// launches fail that check and never block on the box. Call it after the single-instance mutex is
    /// released: the operator may leave the box open while running the install script, whose service
    /// start would otherwise find the mutex taken and exit.
    void ShowLaunchHintIfInteractive();
} // namespace hsm::agent
