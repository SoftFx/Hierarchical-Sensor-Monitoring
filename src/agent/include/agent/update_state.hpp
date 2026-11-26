#pragma once

/// @file
/// @brief Self-update policy that must survive a service restart (#1812): the persistent record of
/// the last update attempt, its backoff, when self-update may run at all, and the hand-off from the
/// update checker to the --apply-update helper.
///
/// Platform-agnostic (no Win32), so it is unit tested on every CI lane.

#include "agent/config.hpp"

#include <cstdint>
#include <filesystem>
#include <functional>
#include <string>

namespace hsm::agent
{
    // --- When self-update runs at all ----------------------------------------------------------

    enum class SelfUpdateGate
    {
        Enabled,
        DisabledByConfig,        ///< `update.enabled` is false: the machine opted out
        DisabledUntrustedServer, ///< `server.allowUntrustedCertificate` is true: see below
    };

    /// Whether this agent may self-update. The update channel never skips TLS certificate validation
    /// (epic #1174): a binary that runs as LocalSystem must come from a server whose certificate the
    /// machine trusts. So an agent configured to accept an untrusted server certificate (for its data)
    /// does not self-update at all; sending data is unaffected.
    SelfUpdateGate SelfUpdateGateFor(const AgentConfig& config);

    /// The line the agent logs once at start when `gate` disables self-update for a reason the
    /// operator may not expect; empty when there is nothing to say.
    std::string SelfUpdateGateMessage(SelfUpdateGate gate);

    // --- Persistent record of the last update attempt ------------------------------------------
    //
    // The checker records an attempt BEFORE it stops the service for the swap, and the helper clears
    // the record once the new version passed its health gate. So a record that names the version the
    // server offers means that version was tried and the agent is still on the old one: the swap,
    // the start or the health gate failed, or the helper never ran. Keyed by the target version: a
    // newer staged version is tried at once. Stored as a few `key=value` lines in
    // `%ProgramData%\HSM Agent\update-state.txt` (UpdateStatePath()); anything unreadable counts as
    // no record.

    struct UpdateAttemptRecord
    {
        std::string version;       ///< target version of the attempt; empty = no record
        int attempts = 0;          ///< attempts made for `version`, >= 1 when recorded
        std::int64_t last_unix = 0; ///< wall-clock time of the last attempt, seconds since the epoch
    };

    /// Wait after `attempts` failed attempts of one version before the next: 1 h, doubled per
    /// further attempt, capped at 24 h. 0 for no attempts.
    std::int64_t UpdateBackoffSeconds(int attempts);

    /// True when an update to `target_version` may be attempted at `now_unix`: there is no record for
    /// that version, or its backoff has elapsed. A clock that went backwards (now before the last
    /// attempt) counts as elapsed, so a wrong clock cannot block updates for good.
    bool UpdateAttemptDue(const UpdateAttemptRecord& record, const std::string& target_version, std::int64_t now_unix);

    /// The record after one more attempt to `target_version` at `now_unix`.
    UpdateAttemptRecord NextUpdateAttempt(const UpdateAttemptRecord& record, const std::string& target_version, std::int64_t now_unix);

    std::string FormatUpdateAttemptRecord(const UpdateAttemptRecord& record);

    /// Parses FormatUpdateAttemptRecord output; false (and an empty `out`) for anything else.
    bool ParseUpdateAttemptRecord(const std::string& text, UpdateAttemptRecord& out);

    /// Reads the record at `path`; an absent or unreadable file is an empty record.
    UpdateAttemptRecord LoadUpdateAttemptRecord(const std::filesystem::path& path);

    /// Writes the record to `path` (via a temp file + rename). False when it could not be written.
    bool SaveUpdateAttemptRecord(const std::filesystem::path& path, const UpdateAttemptRecord& record);

    /// Removes the record. An absent file is success.
    bool ClearUpdateAttemptRecord(const std::filesystem::path& path);

    // --- Hand-off from the checker to the --apply-update helper --------------------------------

    /// The last step of the update checker, once the download is verified and staged at `staged_exe`.
    /// Stops the service for the swap only when all of these hold, in this order:
    ///   1. the staged exe exists (nothing staged -> no stop);
    ///   2. the attempt is recorded at `state_path` (no record -> no stop: a failure would then not
    ///      back off across the restart);
    ///   3. `spawn_helper` started the detached --apply-update helper.
    /// Then calls `request_stop` and returns true. On any failure returns false with `error` set, does
    /// not call `request_stop`, and removes the staged exe.
    bool HandOffStagedUpdate(const std::filesystem::path& staged_exe, const std::filesystem::path& state_path,
                             const std::string& target_version, std::int64_t now_unix,
                             const std::function<bool()>& spawn_helper, const std::function<void()>& request_stop,
                             std::string& error);
} // namespace hsm::agent
