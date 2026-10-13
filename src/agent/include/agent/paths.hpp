#pragma once

/// @file
/// @brief Well-known agent paths under %ProgramData%/%ProgramFiles% and a UTF-8 wide-path file
/// reader.

#include <string>
#include <vector>

namespace hsm::agent
{
    // --- Data / config paths (%ProgramData%) -------------------------------------------------------

    /// `%ProgramData%\HSM Agent`.
    std::wstring ProgramDataAgentDir();

    /// `%ProgramData%\HSM Agent\config.json` — the default config location.
    std::wstring DefaultConfigPath();

    /// `%ProgramData%\HSM Agent\logs\hsm-agent.log`.
    std::wstring LogFilePath();

    /// Create the agent data dir + its `logs` subdir if missing, and (re)apply the protected
    /// SYSTEM + Administrators DACL to both (#1279, `ApplyAgentDirectoryDacl`). Runs on every start, so
    /// an install made before #1279 (data dir inheriting `BUILTIN\Users` read from %ProgramData%) is
    /// tightened on its next service start without touching config.json's contents.
    ///
    /// Best-effort; returns false only when the data dir does not exist afterwards. A failed ACL step
    /// is NOT a hard failure — the service must still read its config and run — but each one is
    /// appended to `acl_errors` (when non-null) for the caller to log at Error level.
    bool EnsureDirectories(std::vector<std::string>* acl_errors = nullptr);

    /// Read a whole text file by wide path into `out` (raw bytes; the config is UTF-8). Returns false
    /// if the file cannot be opened.
    bool ReadTextFileWide(const std::wstring& path, std::string& out);

    // --- Install paths (%ProgramFiles%) — used by self-update (epic #1174) -----------------------

    /// `%ProgramFiles%\HSM Agent` — the directory the install script places the exe in.
    std::wstring ProgramFilesAgentDir();

    /// `%ProgramFiles%\HSM Agent\hsm-agent.exe` — the currently-installed (active) binary.
    std::wstring InstallExePath();

    /// `%ProgramFiles%\HSM Agent\hsm-agent.new.exe` — downloaded update staged here before swap.
    std::wstring NewExePath();

    /// `%ProgramFiles%\HSM Agent\hsm-agent.old.exe` — the previous binary kept until health gate.
    std::wstring OldExePath();
} // namespace hsm::agent
