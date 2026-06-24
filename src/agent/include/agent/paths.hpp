#pragma once

/// @file
/// @brief Well-known agent paths under %ProgramData% and a UTF-8 wide-path file reader.

#include <string>

namespace hsm::agent
{
    /// `%ProgramData%\HSM Agent`.
    std::wstring ProgramDataAgentDir();

    /// `%ProgramData%\HSM Agent\config.json` — the default config location.
    std::wstring DefaultConfigPath();

    /// `%ProgramData%\HSM Agent\logs\hsm-agent.log`.
    std::wstring LogFilePath();

    /// Create the agent data dir + its `logs` subdir if missing. Best-effort; returns false only on a
    /// hard failure.
    bool EnsureDirectories();

    /// Read a whole text file by wide path into `out` (raw bytes; the config is UTF-8). Returns false
    /// if the file cannot be opened.
    bool ReadTextFileWide(const std::wstring& path, std::string& out);
} // namespace hsm::agent
