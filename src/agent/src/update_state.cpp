#include "agent/update_state.hpp"

#include <fstream>
#include <sstream>
#include <system_error>

namespace hsm::agent
{
    namespace
    {
        constexpr std::int64_t kFirstBackoffSeconds = 60 * 60;      // 1 h after the first failed attempt
        constexpr std::int64_t kMaxBackoffSeconds = 24 * 60 * 60;   // never more than a day apart

        bool ParseInt64(const std::string& text, std::int64_t& out)
        {
            if (text.empty())
                return false;
            std::int64_t value = 0;
            for (const char c : text)
            {
                if (c < '0' || c > '9')
                    return false;
                if (value > (INT64_MAX - (c - '0')) / 10)
                    return false;
                value = value * 10 + (c - '0');
            }
            out = value;
            return true;
        }
    } // namespace

    SelfUpdateGate SelfUpdateGateFor(const AgentConfig& config)
    {
        if (!config.update_enabled)
            return SelfUpdateGate::DisabledByConfig;
        if (config.allow_untrusted_certificate)
            return SelfUpdateGate::DisabledUntrustedServer;
        return SelfUpdateGate::Enabled;
    }

    std::string SelfUpdateGateMessage(SelfUpdateGate gate)
    {
        if (gate != SelfUpdateGate::DisabledUntrustedServer)
            return {};
        return "update: self-update is disabled because server.allowUntrustedCertificate is true in config.json: "
               "the agent installs a new binary only from a server whose TLS certificate this machine trusts. "
               "Sending data is not affected. To get updates, give the server a trusted certificate and set "
               "allowUntrustedCertificate to false.";
    }

    std::int64_t UpdateBackoffSeconds(int attempts)
    {
        if (attempts <= 0)
            return 0;
        std::int64_t wait = kFirstBackoffSeconds;
        for (int i = 1; i < attempts && wait < kMaxBackoffSeconds; ++i)
            wait *= 2;
        return wait < kMaxBackoffSeconds ? wait : kMaxBackoffSeconds;
    }

    bool UpdateAttemptDue(const UpdateAttemptRecord& record, const std::string& target_version, std::int64_t now_unix)
    {
        if (record.version.empty() || record.version != target_version || record.attempts <= 0)
            return true;
        if (now_unix < record.last_unix)
            return true; // the clock went backwards
        return now_unix - record.last_unix >= UpdateBackoffSeconds(record.attempts);
    }

    UpdateAttemptRecord NextUpdateAttempt(const UpdateAttemptRecord& record, const std::string& target_version, std::int64_t now_unix)
    {
        UpdateAttemptRecord next;
        next.version = target_version;
        next.attempts = (record.version == target_version && record.attempts > 0) ? record.attempts + 1 : 1;
        next.last_unix = now_unix;
        return next;
    }

    std::string FormatUpdateAttemptRecord(const UpdateAttemptRecord& record)
    {
        std::ostringstream text;
        text << "version=" << record.version << '\n'
             << "attempts=" << record.attempts << '\n'
             << "last=" << record.last_unix << '\n';
        return text.str();
    }

    bool ParseUpdateAttemptRecord(const std::string& text, UpdateAttemptRecord& out)
    {
        out = UpdateAttemptRecord{};
        UpdateAttemptRecord parsed;
        bool has_version = false, has_attempts = false, has_last = false;

        std::istringstream lines(text);
        std::string line;
        while (std::getline(lines, line))
        {
            if (!line.empty() && line.back() == '\r')
                line.pop_back();
            if (line.empty())
                continue;
            const std::size_t eq = line.find('=');
            if (eq == std::string::npos)
                return false;
            const std::string key = line.substr(0, eq);
            const std::string value = line.substr(eq + 1);
            std::int64_t number = 0;
            if (key == "version")
            {
                parsed.version = value;
                has_version = !value.empty();
            }
            else if (key == "attempts")
            {
                if (!ParseInt64(value, number) || number < 1 || number > 1000000)
                    return false;
                parsed.attempts = static_cast<int>(number);
                has_attempts = true;
            }
            else if (key == "last")
            {
                if (!ParseInt64(value, number))
                    return false;
                parsed.last_unix = number;
                has_last = true;
            }
            // Unknown keys are ignored, so a later agent may add some.
        }

        if (!has_version || !has_attempts || !has_last)
            return false;
        out = parsed;
        return true;
    }

    UpdateAttemptRecord LoadUpdateAttemptRecord(const std::filesystem::path& path)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream)
            return {};
        std::ostringstream buffer;
        buffer << stream.rdbuf();
        UpdateAttemptRecord record;
        ParseUpdateAttemptRecord(buffer.str(), record);
        return record;
    }

    bool SaveUpdateAttemptRecord(const std::filesystem::path& path, const UpdateAttemptRecord& record)
    {
        std::filesystem::path temp = path;
        temp += ".tmp";
        {
            std::ofstream stream(temp, std::ios::binary | std::ios::trunc);
            if (!stream)
                return false;
            stream << FormatUpdateAttemptRecord(record);
            stream.flush();
            if (!stream)
                return false;
        }
        std::error_code ec;
        std::filesystem::rename(temp, path, ec); // replaces an existing record
        if (ec)
        {
            std::filesystem::remove(temp, ec);
            return false;
        }
        return true;
    }

    bool ClearUpdateAttemptRecord(const std::filesystem::path& path)
    {
        std::error_code ec;
        std::filesystem::remove(path, ec);
        return !ec;
    }

    bool HandOffStagedUpdate(const std::filesystem::path& staged_exe, const std::filesystem::path& state_path,
                             const std::string& target_version, std::int64_t now_unix,
                             const std::function<bool()>& spawn_helper, const std::function<void()>& request_stop,
                             std::string& error)
    {
        std::error_code ec;
        if (staged_exe.empty() || !std::filesystem::is_regular_file(staged_exe, ec))
        {
            error = "update: nothing staged to apply; the service keeps running";
            return false;
        }

        const auto discard_staged = [&staged_exe] {
            std::error_code ignored;
            std::filesystem::remove(staged_exe, ignored);
        };

        const UpdateAttemptRecord next = NextUpdateAttempt(LoadUpdateAttemptRecord(state_path), target_version, now_unix);
        if (!SaveUpdateAttemptRecord(state_path, next))
        {
            error = "update: cannot record the update attempt in " + state_path.u8string() +
                    "; not updating, so a failed update cannot repeat on every restart";
            discard_staged();
            return false;
        }

        if (!spawn_helper || !spawn_helper())
        {
            error = "update: cannot start the --apply-update helper; the service keeps running";
            discard_staged();
            return false;
        }

        if (request_stop)
            request_stop();
        return true;
    }
} // namespace hsm::agent
