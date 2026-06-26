#pragma once

/// @file
/// @brief Silent SCM install/uninstall for the HSMAgent service (auto-start + delayed +
/// failure-actions). Console-prompt-free: results are exit codes + stderr messages.

#include <string>

namespace hsm::agent
{
    constexpr const wchar_t* kServiceName = L"HSMAgent";
    constexpr const wchar_t* kServiceDisplayName = L"HSM Agent";
    constexpr const wchar_t* kSingleInstanceMutex = L"Global\\HSMAgent";

    /// Full path of the running executable.
    std::wstring CurrentExePath();

    /// True if the current process token is elevated (required for SCM + Event Log registry writes).
    bool IsElevated();

    /// Create (or reconfigure) the HSMAgent service: SERVICE_AUTO_START + delayed-start + restart
    /// failure-actions, and register the Event Log source. Returns 0 on success, non-zero on failure.
    int InstallService();

    /// Stop (if running) and delete the HSMAgent service, and remove the Event Log source. Idempotent.
    /// Returns 0 on success (or already-absent), non-zero on failure.
    int UninstallService();
} // namespace hsm::agent
