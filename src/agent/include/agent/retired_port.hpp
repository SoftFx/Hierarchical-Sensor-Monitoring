#pragma once

/// @file
/// @brief What the agent does when config.json still names a retired HSM server port (#1655).
///
/// Before #1590 an HSM server answered on two ports: 44330 (Sensor API) and 44333 (Web UI). Since
/// #1590 one port, 443, serves everything, and the server stack no longer publishes the old two.
/// An agent installed against the old scheme keeps `server.port: 44330` in its config.json, and a
/// connection that never reaches the server leaves no trace there, so the agent is the only place
/// that can say what is wrong. This header holds the portable part: which ports are retired, the
/// decision made from two reachability probes, and the operator-facing log lines. The probes and
/// the config rewrite live in the runtime (Windows).

#include <functional>
#include <string>

namespace hsm::agent
{
    /// The one port an HSM server answers on since #1590.
    inline constexpr int kStandardServerPort = 443;

    /// True for 44330 and 44333, the ports of the pre-#1590 two-port scheme.
    bool IsRetiredServerPort(int port) noexcept;

    enum class RetiredPortVerdict
    {
        NotRetired,        ///< the configured port is not a retired one: nothing to check
        StillAnswers,      ///< an HSM server answers on the retired port: keep using it
        SwitchToStandard,  ///< the retired port is dead and an HSM server answers on 443: move there
        ServerUnreachable, ///< neither port answers: keep the config, tell the operator what to check
    };

    /// Returns true when an HSM Sensor API answers on `port` of the configured server host.
    using ServerPortProbe = std::function<bool(int port)>;

    /// Decide what to do about the configured port. Probes lazily and in a fixed order: nothing is
    /// probed for a port that is not retired (such a config behaves exactly as before), and 443 is
    /// probed only after the retired port failed to answer, so a server that still serves the old
    /// port (a proxy-less deployment pinned to it, or an operator's own port mapping) is never
    /// second-guessed.
    RetiredPortVerdict JudgeRetiredPort(int configured_port, const ServerPortProbe& hsm_answers_on);

    /// Logged once at every start while config.json names a retired port.
    std::string RetiredPortStartupNotice(int configured_port);

    /// Logged when the agent rewrites config.json to port 443 and restarts to apply it.
    std::string RetiredPortSwitchNotice(int configured_port);

    /// Logged when the retired port is dead, 443 answers, and the agent cannot make the move
    /// itself (a foreground `--console` run, or a config.json it failed to rewrite).
    std::string RetiredPortManualMoveNotice(int configured_port);

    /// Logged while neither the retired port nor 443 answers.
    std::string RetiredPortUnreachableNotice(int configured_port);
} // namespace hsm::agent
