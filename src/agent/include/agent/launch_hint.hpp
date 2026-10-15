#pragma once

/// @file
/// @brief Guidance shown when a person starts hsm-agent.exe by hand without a mode (#1559).

#include <string>

namespace hsm::agent
{
    /// Title of the interactive-launch message box.
    inline constexpr const wchar_t* kLaunchHintTitle = L"HSM Agent";

    /// Text of the message box shown when the exe was started interactively with no mode (typically
    /// a double-click on the downloaded hsm-agent.exe): what the exe is, how to install it, how to run
    /// it in the foreground, and where the logs are. `version` is the ASCII HSM_AGENT_VERSION string.
    /// Pure and platform-agnostic, so the wording is unit-tested without any UI.
    std::wstring BuildInteractiveLaunchMessage(const std::string& version);
} // namespace hsm::agent
