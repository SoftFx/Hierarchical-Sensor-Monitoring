#include "agent/launch_hint.hpp"

namespace hsm::agent
{
    std::wstring BuildInteractiveLaunchMessage(const std::string& version)
    {
        // HSM_AGENT_VERSION is ASCII (x.y.z), so a per-char widening is exact.
        const std::wstring wide_version(version.begin(), version.end());

        return L"HSM Agent " + wide_version +
               L" is a Windows service that streams this computer's metrics to an HSM server. "
               L"It is started by Windows, not by double-clicking it.\n\n"
               L"To install, run install-hsmagent-service.cmd from the downloaded bundle as Administrator.\n\n"
               L"For a foreground run: hsm-agent.exe --console --config <path>\n\n"
               L"Logs: %ProgramData%\\HSM Agent\\logs\n\n"
               L"See README.txt in the downloaded bundle for details.";
    }
} // namespace hsm::agent
