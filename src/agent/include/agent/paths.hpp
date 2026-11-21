#pragma once

/// @file
/// @brief Well-known agent paths (data under %ProgramData%, install files next to the running exe)
/// and a UTF-8 wide-path file reader.

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

    /// `%ProgramData%\HSM Agent\update-state.txt` — the persistent record of the last self-update
    /// attempt (#1812, see update_state.hpp), so a version that failed to apply backs off across
    /// service restarts instead of being retried in a loop.
    std::wstring UpdateStatePath();

    // --- Install paths — used by self-update (epic #1174, #1812) ---------------------------------
    //
    // The install directory is the directory of the RUNNING exe, never %ProgramFiles%: a service
    // installed elsewhere (e.g. a 64-bit exe under "Program Files (x86)", whose process sees the other
    // folder as %ProgramFiles%) must stage, swap, roll back and clean up in its own folder. The update
    // checker and the --apply-update helper agree on it by construction: the helper is spawned from the
    // running exe, so both derive the same directory.

    /// Full path of the running executable (GetModuleFileNameW), or empty on failure.
    std::wstring CurrentExePath();

    /// The files one self-update touches. All empty when the install directory is unknown; callers
    /// treat that as "cannot update".
    struct UpdateFiles
    {
        std::wstring dir;     ///< the install directory: the directory of the running exe
        std::wstring exe;     ///< the active binary: the running exe itself
        std::wstring new_exe; ///< `<dir>\hsm-agent.new.exe`: the verified download, staged before the swap
        std::wstring old_exe; ///< `<dir>\hsm-agent.old.exe`: the previous binary, the rollback target
    };

    /// The update files for an agent whose exe is `running_exe` (a full path). Pure.
    UpdateFiles UpdateFilesFor(const std::wstring& running_exe);

    /// `UpdateFilesFor(CurrentExePath())`.
    UpdateFiles CurrentUpdateFiles();

    /// The currently-installed (active) binary: the running exe itself.
    std::wstring InstallExePath();

    /// `<dir of the running exe>\hsm-agent.new.exe`.
    std::wstring NewExePath();

    /// `<dir of the running exe>\hsm-agent.old.exe`; deleted by the next start after a swap.
    std::wstring OldExePath();
} // namespace hsm::agent
