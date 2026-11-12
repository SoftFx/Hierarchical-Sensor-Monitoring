#include "agent/retired_port.hpp"

#include "agent/config.hpp"

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

        if (hsm_answers_on(configured_port) != PortAnswer::Nothing)
            return RetiredPortVerdict::StillAnswers;

        switch (hsm_answers_on(kStandardServerPort))
        {
        case PortAnswer::HsmServer:
            return RetiredPortVerdict::SwitchToStandard;
        case PortAnswer::Unidentified:
            return RetiredPortVerdict::UnrecognisedOnStandard;
        case PortAnswer::Nothing:
            break;
        }
        return RetiredPortVerdict::ServerUnreachable;
    }

    RetiredPortStep NextRetiredPortStep(RetiredPortWatchState& state, RetiredPortVerdict verdict, bool can_move)
    {
        state.repeats = (verdict == state.last_verdict) ? state.repeats + 1 : 0;
        state.last_verdict = verdict;

        RetiredPortStep step;
        switch (verdict)
        {
        case RetiredPortVerdict::ServerUnreachable:
            if (state.repeats % kRetiredPortNoticeEveryChecks == 0)
                step.action = RetiredPortAction::ReportUnreachable;
            break;

        case RetiredPortVerdict::UnrecognisedOnStandard:
            if (state.repeats % kRetiredPortNoticeEveryChecks == 0)
                step.action = RetiredPortAction::ReportUnrecognised;
            break;

        case RetiredPortVerdict::SwitchToStandard:
        {
            if (state.repeats == 0)
            {
                // First sighting: confirm soon, change nothing yet.
                step.next_delay = kRetiredPortConfirmDelay;
                break;
            }
            // Confirmed. Whatever cannot be done by the agent itself is reported from the
            // confirming check on, hourly.
            const bool notice_due = (state.repeats - 1) % kRetiredPortNoticeEveryChecks == 0;
            if (can_move)
            {
                step.action = RetiredPortAction::Move;
                step.failure_notice_due = notice_due;
            }
            else if (notice_due)
            {
                step.action = RetiredPortAction::ReportManualMove;
            }
            break;
        }

        case RetiredPortVerdict::NotRetired:
        case RetiredPortVerdict::StillAnswers:
            break;
        }
        return step;
    }

    MoveOutcome MoveConfigToStandardPort(const std::string& path, const std::string& probed_address, int probed_port,
                                         std::string& error)
    {
        AgentConfig current;
        std::string load_error;
        if (!LoadAgentConfig(path, current, load_error))
        {
            error = "reading it back failed: " + load_error;
            return MoveOutcome::Failed;
        }
        // The probes ran against the values the agent started with. An operator who has since
        // pointed the file elsewhere (another port, another host) decides; the agent does not
        // overwrite a choice it never probed.
        if (current.port != probed_port || current.server_address != probed_address)
            return MoveOutcome::ChangedSinceStart;

        current.port = kStandardServerPort;
        return WriteAgentConfig(path, current, error) ? MoveOutcome::Moved : MoveOutcome::Failed;
    }

    std::string RetiredPortStartupNotice(int configured_port, bool can_move)
    {
        return Subject(configured_port) +
               (can_move ? ". The agent keeps using it while the server answers there; once it stops answering and the"
                           " server answers on 443, the HSMAgent service moves to 443 by itself. To move now, set"
                           " \"port\": 443 under \"server\" in config.json and restart the HSMAgent service."
                         : ". The agent keeps using it while the server answers there. A --console run never changes"
                           " config.json: set \"port\": 443 under \"server\" in config.json and restart the agent.");
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

    std::string RetiredPortUnreachableNotice(int configured_port, bool can_move)
    {
        return Subject(configured_port) +
               " and no HSM server answers there or on 443, so nothing is being delivered. Check that the"
               " server is up and that server.address in config.json is right" +
               (can_move ? std::string("; the HSMAgent service moves to 443 by itself as soon as the server answers there.")
                         : std::string("; a --console run never changes config.json, so set \"port\": 443 under"
                                       " \"server\" there yourself once the server answers on 443."));
    }

    std::string RetiredPortUnrecognisedNotice(int configured_port)
    {
        return Subject(configured_port) +
               " and no longer answers, so nothing is being delivered. Something answers on 443 of the same host,"
               " but not as an HSM server of this release or later (an older HSM server, or another service), so"
               " the agent does not move there. If it is your HSM server, set \"port\": 443 under \"server\" in"
               " config.json and restart the agent; updating the server makes the agent move by itself.";
    }

    std::string RetiredPortChangedSinceStartNotice(int configured_port)
    {
        return Subject(configured_port) +
               " and no longer answers, but config.json no longer names that address and port: it was changed"
               " after the agent started, so the agent leaves it as it is. Restart the HSMAgent service to apply it.";
    }
} // namespace hsm::agent
