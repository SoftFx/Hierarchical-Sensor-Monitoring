#pragma once

/// @file
/// @brief SCM service entry. Hands the process to the service control dispatcher and runs the
/// AgentRuntime on a worker thread until the SCM stops it.

#include <string>

namespace hsm::agent
{
    /// Connect to the service control dispatcher and run the HSMAgent service, loading config from
    /// `config_path`. Returns the process exit code. If the process was not launched by the SCM,
    /// returns a distinct non-zero code and prints guidance (use --console instead).
    int RunService(const std::wstring& config_path);
} // namespace hsm::agent
