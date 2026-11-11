#include "agent/retired_port.hpp"

namespace hsm::agent
{
    namespace
    {
        constexpr int kRetiredSensorPort = 44330;
        constexpr int kRetiredSitePort = 44333;

        std::string Subject(int configured_port)
        {
            return "config: server.port " + std::to_string(configured_port) +
                   " is a retired HSM server port (44330/44333 were replaced by the single port 443)";
        }
    } // namespace

    bool IsRetiredServerPort(int port) noexcept
    {
        return port == kRetiredSensorPort || port == kRetiredSitePort;
    }

    RetiredPortVerdict JudgeRetiredPort(int configured_port, const ServerPortProbe& hsm_answers_on)
    {
        if (!IsRetiredServerPort(configured_port))
            return RetiredPortVerdict::NotRetired;

        if (hsm_answers_on(configured_port))
            return RetiredPortVerdict::StillAnswers;

        return hsm_answers_on(kStandardServerPort) ? RetiredPortVerdict::SwitchToStandard
                                                   : RetiredPortVerdict::ServerUnreachable;
    }

    std::string RetiredPortStartupNotice(int configured_port)
    {
        return Subject(configured_port) +
               ". The agent keeps using it while the server answers there; once it stops answering and the server"
               " answers on 443, the HSMAgent service moves to 443 by itself. To move now, set \"port\": 443 under \"server\""
               " in config.json and restart the HSMAgent service.";
    }

    std::string RetiredPortSwitchNotice(int configured_port)
    {
        return Subject(configured_port) +
               " and no longer answers; the server answers on 443. config.json now says \"port\": 443;"
               " restarting to apply.";
    }

    std::string RetiredPortManualMoveNotice(int configured_port)
    {
        return Subject(configured_port) +
               " and no longer answers, so nothing is being delivered; the server answers on 443."
               " Set \"port\": 443 under \"server\" in config.json and restart the agent.";
    }

    std::string RetiredPortUnreachableNotice(int configured_port)
    {
        return Subject(configured_port) +
               " and the server answers neither there nor on 443, so nothing is being delivered. Check that the"
               " server is up and that server.address in config.json is right; the HSMAgent service moves to"
               " 443 by itself as soon as the server answers there.";
    }
} // namespace hsm::agent
