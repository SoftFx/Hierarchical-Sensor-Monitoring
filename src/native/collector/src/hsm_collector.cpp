#include "hsm_collector/hsm_collector.h"

#include <algorithm>
#include <atomic>
#include <charconv>
#include <chrono>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <ctime>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <exception>
#include <iomanip>
#include <functional>
#include <initializer_list>
#include <limits>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#if defined(HSM_COLLECTOR_HTTP)
#include "hsm_http_endpoints.hpp"
#include "hsm_http_transport.hpp"
#endif

namespace
{
    constexpr size_t MaxCommentLength = 1024;

    // Full lifecycle state machine (overview.md "Lifecycle"), mirroring the managed
    // CollectorStatus: Stopped -> Starting -> Running -> Stopping -> Stopped, and
    // Any-except-Disposed -> Disposed (terminal). Starting/Stopping are transient —
    // they are only observable from another thread while Start()/Stop() runs.
    enum class CollectorState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Disposed,
    };

    hsm_collector_status_t ToPublicStatus(CollectorState state)
    {
        switch (state)
        {
        case CollectorState::Stopped:
            return HSM_COLLECTOR_STATUS_STOPPED;
        case CollectorState::Starting:
            return HSM_COLLECTOR_STATUS_STARTING;
        case CollectorState::Running:
            return HSM_COLLECTOR_STATUS_RUNNING;
        case CollectorState::Stopping:
            return HSM_COLLECTOR_STATUS_STOPPING;
        case CollectorState::Disposed:
            return HSM_COLLECTOR_STATUS_DISPOSED;
        }

        return HSM_COLLECTOR_STATUS_STOPPED;
    }

    // A registered lifecycle observer (portable ILifecycleListener). The callback is
    // invoked exception-isolated; user_data must outlive the collector.
    struct LifecycleListener
    {
        hsm_lifecycle_callback_t callback;
        void* user_data;
    };

    // One deduplicated error message: how many occurrences have been collapsed since
    // the last emit, and the system-clock time of that emit (for window expiry).
    struct DedupEntry
    {
        int64_t suppressed;
        int64_t last_logged_ms;
    };

    std::string CopyString(const char* value)
    {
        return value == nullptr ? std::string{} : std::string{ value };
    }

    std::string TrimComment(std::string comment)
    {
        if (comment.size() > MaxCommentLength)
            comment.resize(MaxCommentLength);

        return comment;
    }

    std::string TrimSlashes(const std::string& value)
    {
        const auto first = value.find_first_not_of('/');
        if (first == std::string::npos)
            return std::string{};

        const auto last = value.find_last_not_of('/');
        return value.substr(first, last - first + 1);
    }

    bool IsBlank(const std::string& value)
    {
        return std::all_of(value.begin(), value.end(), [](unsigned char ch) { return std::isspace(ch) != 0; });
    }

    bool IsValidPath(const char* value)
    {
        if (value == nullptr)
            return false;

        return !IsBlank(TrimSlashes(CopyString(value)));
    }

    bool IsValidStatus(hsm_sensor_status_t status)
    {
        switch (status)
        {
        case HSM_SENSOR_STATUS_OFF_TIME:
        case HSM_SENSOR_STATUS_OK:
        case HSM_SENSOR_STATUS_WARNING:
        case HSM_SENSOR_STATUS_ERROR:
            return true;
        default:
            return false;
        }
    }

    // Internal conformance-format escaping (the conformance corpus' own text convention, NOT the
    // real .NET wire). Quote is the short \", non-ASCII passes through verbatim. The shared corpus
    // pins this exact shape across both drivers; do NOT change it. The real System.Text.Json wire
    // escaping lives in EscapeJsonWire below.
    std::string EscapeJson(const std::string& value)
    {
        std::ostringstream output;
        output << std::hex << std::setfill('0');

        for (const auto ch : value)
        {
            const auto byte = static_cast<unsigned char>(ch);

            switch (ch)
            {
            case '\\':
                output << "\\\\";
                break;
            case '"':
                output << "\\\"";
                break;
            case '\b':
                output << "\\b";
                break;
            case '\f':
                output << "\\f";
                break;
            case '\n':
                output << "\\n";
                break;
            case '\r':
                output << "\\r";
                break;
            case '\t':
                output << "\\t";
                break;
            default:
                if (byte < 0x20)
                    output << "\\u" << std::setw(4) << static_cast<int>(byte);
                else
                    output << ch;
                break;
            }
        }

        return output.str();
    }

    void AppendUnicodeEscape(std::ostringstream& output, unsigned int code_unit)
    {
        // Four-digit UPPERCASE hex, matching System.Text.Json (e.g. "<", "é").
        output << "\\u" << std::setw(4) << (code_unit & 0xFFFFu);
    }

    // The exact set of ASCII chars that System.Text.Json's DEFAULT JavaScriptEncoder escapes as
    // \uXXXX (observed by serializing every code point through the real HttpRequest<T> options;
    // see WireFormatGoldenLockTests escaping cases). Note '"' is " here, NOT the short \".
    bool NeedsAsciiUnicodeEscape(unsigned char b)
    {
        switch (b)
        {
        case 0x22: // "
        case 0x26: // &
        case 0x27: // '
        case 0x2B: // +
        case 0x3C: // <
        case 0x3E: // >
        case 0x60: // `
        case 0x7F: // DEL
            return true;
        default:
            return false;
        }
    }

    // Byte-identical to System.Text.Json's default encoder: short escapes for \\ \b \t \n \f \r;
    // everything else that must escape (the HTML-sensitive ASCII set, all other controls, DEL, and
    // every non-ASCII code point) becomes \uXXXX in UTF-16 (surrogate pairs for astral planes),
    // uppercase hex. The double quote is " (not \"). Input is UTF-8; malformed sequences emit
    // the replacement character U+FFFD so the serializer never produces invalid JSON. Used by the
    // real-wire BuildWire* serializers; the internal-format EscapeJson above is a different shape.
    std::string EscapeJsonWire(const std::string& value)
    {
        std::ostringstream output;
        output << std::hex << std::uppercase << std::setfill('0');

        const size_t n = value.size();
        for (size_t i = 0; i < n;)
        {
            const unsigned char b = static_cast<unsigned char>(value[i]);

            switch (b)
            {
            case '\\':
                output << "\\\\";
                ++i;
                continue;
            case '\b':
                output << "\\b";
                ++i;
                continue;
            case '\t':
                output << "\\t";
                ++i;
                continue;
            case '\n':
                output << "\\n";
                ++i;
                continue;
            case '\f':
                output << "\\f";
                ++i;
                continue;
            case '\r':
                output << "\\r";
                ++i;
                continue;
            default:
                break;
            }

            if (b < 0x80)
            {
                if (b < 0x20 || NeedsAsciiUnicodeEscape(b))
                    AppendUnicodeEscape(output, b);
                else
                    output << static_cast<char>(b);
                ++i;
                continue;
            }

            // Decode one UTF-8 code point.
            unsigned int cp = 0;
            size_t len = 0;
            if ((b & 0xE0) == 0xC0)
            {
                cp = b & 0x1Fu;
                len = 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                cp = b & 0x0Fu;
                len = 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                cp = b & 0x07u;
                len = 4;
            }
            else
            {
                AppendUnicodeEscape(output, 0xFFFD);
                ++i;
                continue;
            }

            if (i + len > n)
            {
                AppendUnicodeEscape(output, 0xFFFD);
                i = n;
                continue;
            }

            bool ok = true;
            for (size_t k = 1; k < len; ++k)
            {
                const unsigned char cont = static_cast<unsigned char>(value[i + k]);
                if ((cont & 0xC0) != 0x80)
                {
                    ok = false;
                    break;
                }
                cp = (cp << 6) | (cont & 0x3Fu);
            }

            if (!ok)
            {
                AppendUnicodeEscape(output, 0xFFFD);
                ++i;
                continue;
            }

            i += len;

            if (cp <= 0xFFFF)
            {
                AppendUnicodeEscape(output, cp);
            }
            else
            {
                cp -= 0x10000;
                AppendUnicodeEscape(output, 0xD800 + (cp >> 10));
                AppendUnicodeEscape(output, 0xDC00 + (cp & 0x3FF));
            }
        }

        return output.str();
    }

    int64_t UnixTimeMilliseconds()
    {
        const auto now = std::chrono::system_clock::now().time_since_epoch();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    }

    // .NET System.Text.Json (net8/Core) ISO-8601 UTC: "yyyy-MM-ddTHH:mm:ss[.fff]Z" with the
    // fraction trimmed to its minimal non-zero digits (no fraction when zero), e.g.
    // "2026-06-16T12:00:00Z" and "2026-06-16T12:00:00.123Z". Native payload time is
    // unix-millisecond precision, so the fraction is at most three digits. (#1096 wire contract.)
    std::string IsoUtcFromUnixMs(int64_t unix_ms)
    {
        // Floored division so a pre-epoch (negative) input still yields millis in [0,999] and a
        // correctly floored second, instead of C++ truncate-toward-zero producing a negative
        // remainder and an off-by-one second. Production times are post-epoch; this only matters
        // for the manual-clock test seam.
        int64_t secs64 = unix_ms / 1000;
        int64_t millis64 = unix_ms % 1000;
        if (millis64 < 0)
        {
            millis64 += 1000;
            --secs64;
        }

        const std::time_t secs = static_cast<std::time_t>(secs64);
        const int millis = static_cast<int>(millis64);

        std::tm tm{};
#if defined(_WIN32)
        const bool ok = gmtime_s(&tm, &secs) == 0;
#else
        const bool ok = gmtime_r(&secs, &tm) != nullptr;
#endif
        // Out-of-range second: fail loudly with a distinctive, well-formed sentinel rather than
        // silently emitting a zero-initialized "0000-..." timestamp.
        if (!ok)
            return "0001-01-01T00:00:00Z";

        char buf[40];
        std::snprintf(
            buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02d",
            tm.tm_year + 1900, tm.tm_mon + 1, tm.tm_mday, tm.tm_hour, tm.tm_min, tm.tm_sec);

        std::string result(buf);
        if (millis != 0)
        {
            char frac[8];
            std::snprintf(frac, sizeof(frac), "%03d", millis);
            std::string f(frac);
            while (f.size() > 1 && f.back() == '0')
                f.pop_back();
            result += '.';
            result += f;
        }

        result += 'Z';
        return result;
    }

    // .NET Version.ToString(): "major.minor[.build[.revision]]". A trailing component is emitted
    // only when present (>= 0) and only if every earlier optional component is present too — a
    // revision without a build cannot exist (Version forbids it), so build < 0 drops revision.
    std::string VersionString(int32_t major, int32_t minor, int32_t build, int32_t revision)
    {
        std::string text = std::to_string(major) + "." + std::to_string(minor);
        if (build >= 0)
        {
            text += "." + std::to_string(build);
            if (revision >= 0)
                text += "." + std::to_string(revision);
        }
        return text;
    }

    // .NET TimeSpan "c" (constant/invariant) format: "[-][d.]hh:mm:ss[.fffffff]". The day part is
    // omitted when zero; the fraction is present only when non-zero and is ALWAYS seven digits
    // (100-ns ticks, not trimmed — unlike the ISO time fraction). Input is .NET ticks (100 ns).
    // (#1096 §15 wire contract for TimeSpan sensor values.)
    std::string TimeSpanCFormat(int64_t ticks)
    {
        constexpr uint64_t per_second = 10000000ull;
        constexpr uint64_t per_minute = 60ull * per_second;
        constexpr uint64_t per_hour = 60ull * per_minute;
        constexpr uint64_t per_day = 24ull * per_hour;

        std::string sign;
        // Magnitude in uint64_t: negating ticks as int64_t would be UB for INT64_MIN, which is a
        // legal value here (TimeSpan.MinValue.Ticks == Int64.MinValue). Two's-complement magnitude
        // via unsigned arithmetic is well-defined for the whole range.
        uint64_t total;
        if (ticks < 0)
        {
            sign = "-";
            total = 0ull - static_cast<uint64_t>(ticks);
        }
        else
        {
            total = static_cast<uint64_t>(ticks);
        }

        const uint64_t days = total / per_day;
        total %= per_day;
        const uint64_t hours = total / per_hour;
        total %= per_hour;
        const uint64_t minutes = total / per_minute;
        total %= per_minute;
        const uint64_t seconds = total / per_second;
        const uint64_t fraction = total % per_second;

        char buf[32];
        std::snprintf(
            buf, sizeof(buf), "%02llu:%02llu:%02llu",
            static_cast<unsigned long long>(hours), static_cast<unsigned long long>(minutes),
            static_cast<unsigned long long>(seconds));

        std::string result = sign;
        if (days > 0)
            result += std::to_string(days) + ".";
        result += buf;
        if (fraction > 0)
        {
            char frac[16];
            std::snprintf(frac, sizeof(frac), "%07llu", static_cast<unsigned long long>(fraction));
            result += ".";
            result += frac;
        }

        return result;
    }

    // Real .NET wire JSON for a single value DTO (#1096 §15). Byte-identical to net8
    // System.Text.Json output: property order is most-derived-first then base-last with Type
    // first (Type, Value, Comment, Time, Status, Key, Path); Comment null is emitted as `null`;
    // the obsolete Key is always `null`; Time is ISO-8601 Z. `value_json` is pre-formatted by
    // the caller (DoubleJson for doubles, quoted+escaped for strings/timespan/version, bare for
    // ints/bools/enum). Used by the transport in #1096 PR2; the legacy BuildValueJson stays for
    // the behavior corpus.
    std::string BuildWireValueJson(
        hsm_sensor_type_t type,
        const std::string& value_json,
        const std::string& comment,
        bool comment_is_null,
        hsm_sensor_status_t status,
        int64_t time_ms,
        const std::string& path)
    {
        std::ostringstream json;
        json << "{\"Type\":" << static_cast<int>(type)
             << ",\"Value\":" << value_json
             << ",\"Comment\":" << (comment_is_null ? std::string("null") : ("\"" + EscapeJsonWire(comment) + "\""))
             << ",\"Time\":\"" << IsoUtcFromUnixMs(time_ms) << "\""
             << ",\"Status\":" << static_cast<int>(status)
             << ",\"Key\":null"
             << ",\"Path\":\"" << EscapeJsonWire(path) << "\"}";
        return json.str();
    }

    // .NET serializes List<byte> as a numeric JSON array ([104,105]), NOT base64 (#1096 §15).
    std::string ByteArrayJson(const std::string& content)
    {
        std::ostringstream array;
        array << '[';
        for (size_t i = 0; i < content.size(); ++i)
        {
            if (i != 0)
                array << ',';
            array << static_cast<int>(static_cast<unsigned char>(content[i]));
        }
        array << ']';
        return array.str();
    }

    // Real wire JSON for FileSensorValue: Type, Extension, Name, Value(byte array), Comment,
    // Time, Status, Key, Path (#1096 §15).
    std::string BuildWireFileJson(
        const std::string& extension,
        const std::string& name,
        const std::string& content_utf8,
        const std::string& comment,
        bool comment_is_null,
        hsm_sensor_status_t status,
        int64_t time_ms,
        const std::string& path)
    {
        std::ostringstream json;
        json << "{\"Type\":" << static_cast<int>(HSM_SENSOR_TYPE_FILE)
             << ",\"Extension\":\"" << EscapeJsonWire(extension) << "\""
             << ",\"Name\":\"" << EscapeJsonWire(name) << "\""
             << ",\"Value\":" << ByteArrayJson(content_utf8)
             << ",\"Comment\":" << (comment_is_null ? std::string("null") : ("\"" + EscapeJsonWire(comment) + "\""))
             << ",\"Time\":\"" << IsoUtcFromUnixMs(time_ms) << "\""
             << ",\"Status\":" << static_cast<int>(status)
             << ",\"Key\":null"
             << ",\"Path\":\"" << EscapeJsonWire(path) << "\"}";
        return json.str();
    }

    // Monotonic clock for periodic scheduling and rate elapsed-time math (mirrors the C#
    // collector's Stopwatch-based scheduler/rate sensor; wall-clock jumps must not skew rates).
    int64_t SteadyMilliseconds()
    {
        const auto now = std::chrono::steady_clock::now().time_since_epoch();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    }

    // Injectable clock seam (issue #1095 §13). The collector reads time for periodic
    // scheduling through this so a test can drive virtual time; the default RealClock
    // leaves production behavior unchanged. Only the scheduler / periodic-due path is
    // routed through it — payload timestamps and bar alignment keep the real wall clock
    // (there are no conformance time-control verbs, by design — see the spike journal).
    class Clock
    {
    public:
        virtual ~Clock() = default;
        virtual int64_t SteadyNowMs() const = 0;
        virtual int64_t SystemNowMs() const = 0;
    };

    class RealClock : public Clock
    {
    public:
        int64_t SteadyNowMs() const override
        {
            return SteadyMilliseconds();
        }
        int64_t SystemNowMs() const override
        {
            return UnixTimeMilliseconds();
        }
    };

    class ManualClock : public Clock
    {
    public:
        explicit ManualClock(int64_t steady = 0, int64_t system = 0)
            : steady_(steady), system_(system)
        {
        }
        int64_t SteadyNowMs() const override
        {
            return steady_.load();
        }
        int64_t SystemNowMs() const override
        {
            return system_.load();
        }
        void AdvanceMs(int64_t delta)
        {
            steady_ += delta;
            system_ += delta;
        }

    private:
        std::atomic<int64_t> steady_;
        std::atomic<int64_t> system_;
    };

    // Host-callback isolation: a throwing callback may neither cross the C ABI boundary nor
    // break the component that invoked it — lifecycle listeners, log sinks, scheduler actions
    // and onError. overview.md "Callback exception isolation".
    template <typename Fn>
    void InvokeIsolated(Fn&& fn) noexcept
    {
        try
        {
            fn();
        }
        catch (...)
        {
            // Swallowed by contract.
        }
    }

    // Single-worker periodic task — the portable ScheduledTaskHandle (issue #1095 §13).
    // Idempotent Start/Stop; the worker sleeps until the next due time reported by the
    // injected clock (event-driven, not a busy poll — bounded by kMaxWaitMs so a dynamic
    // schedule change or virtual-clock jump is still picked up), runs the action with no
    // overlap (one worker, sequential) and exception isolation, and Stop joins after at
    // most one in-flight action (bounded wait-for-current-run).
    class ScheduledTask
    {
    public:
        // now_ms reports the current monotonic time through the collector's (swappable) clock;
        // next_due_ms reports the next absolute due time; action is the work to run when due.
        ScheduledTask(std::function<int64_t()> now_ms, std::function<int64_t()> next_due_ms, std::function<void()> action)
            : now_ms_(std::move(now_ms)), next_due_ms_(std::move(next_due_ms)), action_(std::move(action))
        {
        }

        ScheduledTask(const ScheduledTask&) = delete;
        ScheduledTask& operator=(const ScheduledTask&) = delete;

        ~ScheduledTask()
        {
            Stop();
        }

        void Start()
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (running_)
                return;

            stop_ = false;
            running_ = true;
            worker_ = std::thread([this] { Loop(); });
        }

        void Stop()
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (!running_)
                    return;

                stop_ = true;
            }

            cv_.notify_all();
            if (worker_.joinable())
                worker_.join();

            std::lock_guard<std::mutex> guard(mutex_);
            running_ = false;
        }

        // Re-evaluate the schedule now (e.g. a periodic sensor was added, or virtual time
        // advanced). Cheap and lock-free against the worker — just nudges the wait.
        void Wake()
        {
            cv_.notify_all();
        }

    private:
        static constexpr int64_t kMaxWaitMs = 1000;

        void Loop()
        {
            std::unique_lock<std::mutex> lock(mutex_);

            while (!stop_)
            {
                const int64_t now = now_ms_();
                const int64_t due = next_due_ms_();

                if (now >= due)
                {
                    lock.unlock();
                    InvokeIsolated(action_);
                    lock.lock();
                    continue;
                }

                // due > now here. Guard the subtraction against overflow when due is the
                // "nothing scheduled" sentinel (int64_max) or the (virtual) clock is far
                // negative — just cap the sleep at kMaxWaitMs.
                int64_t wait_ms = kMaxWaitMs;
                if (due < (std::numeric_limits<int64_t>::max)())
                    wait_ms = std::min<int64_t>(due - now, kMaxWaitMs);

                cv_.wait_for(lock, std::chrono::milliseconds(wait_ms), [this] { return stop_; });
            }
        }

        std::function<int64_t()> now_ms_;
        std::function<int64_t()> next_due_ms_;
        std::function<void()> action_;

        std::mutex mutex_;
        std::condition_variable cv_;
        std::thread worker_;
        bool running_ = false;
        bool stop_ = false;
    };

    // .NET shortest round-trip ("R") double text — the canonical payload contract
    // (tests/conformance/README.md). std::to_chars produces the shortest digits, but its
    // fixed/scientific choice differs from .NET (e.g. 1e5: to_chars "1e+05", .NET "100000"),
    // so the digits are extracted from the scientific form and reassembled with .NET rules:
    // fixed notation iff the decimal exponent is in [-4, 14], otherwise "dE±XX" with an
    // uppercase 'E' and a sign-prefixed exponent of at least two digits.
    std::string DoubleJson(double value)
    {
        char buffer[64];
        const auto result = std::to_chars(buffer, buffer + sizeof(buffer), value, std::chars_format::scientific);
        const std::string scientific(buffer, result.ptr);

        const bool negative = scientific[0] == '-';
        const auto exponent_marker = scientific.find('e');
        const int exponent = std::stoi(scientific.substr(exponent_marker + 1));

        std::string digits;
        for (size_t i = negative ? 1 : 0; i < exponent_marker; ++i)
            if (scientific[i] != '.')
                digits.push_back(scientific[i]);

        std::string text;
        if (negative && value != 0.0)
            text.push_back('-');

        if (exponent >= -4 && exponent <= 14)
        {
            if (exponent < 0)
            {
                text += "0.";
                text.append(static_cast<size_t>(-exponent - 1), '0');
                text += digits;
            }
            else if (static_cast<size_t>(exponent) + 1 >= digits.size())
            {
                text += digits;
                text.append(static_cast<size_t>(exponent) + 1 - digits.size(), '0');
            }
            else
            {
                text += digits.substr(0, static_cast<size_t>(exponent) + 1);
                text.push_back('.');
                text += digits.substr(static_cast<size_t>(exponent) + 1);
            }
        }
        else
        {
            text += digits.substr(0, 1);
            if (digits.size() > 1)
            {
                text.push_back('.');
                text += digits.substr(1);
            }

            text.push_back('E');
            text.push_back(exponent < 0 ? '-' : '+');

            const auto magnitude = std::to_string(exponent < 0 ? -exponent : exponent);
            if (magnitude.size() < 2)
                text.push_back('0');
            text += magnitude;
        }

        return text;
    }

    struct EnumOptionData
    {
        int32_t key = 0;
        std::string value;
        int32_t color = 0;
        std::string description;
    };

    // ---- Alert model (mirrors HSMDataCollector.Alerts -> AlertUpdateRequest) -------------------
    // One condition; Target.Value is omitted (serialized null) for a LastValue target.
    struct AlertConditionData
    {
        hsm_alert_combination_t combination = HSM_ALERT_COMBINATION_AND;
        hsm_alert_property_t property = HSM_ALERT_PROP_VALUE;
        hsm_alert_operation_t operation = HSM_ALERT_OP_EQUAL;
        hsm_alert_target_type_t target_type = HSM_ALERT_TARGET_CONST;
        bool target_is_null = true;
        std::string target_value;
    };

    // A built alert. Mirrors AlertUpdateRequest field-by-field. has_* flags distinguish an unset
    // (null on the wire) value from a present one. inactivity is the TTL-alert window only (it is
    // not an AlertUpdateRequest field; it feeds AddOrUpdate.TTLs in ticks).
    struct AlertData
    {
        hsm_alert_kind_t kind = HSM_ALERT_KIND_INSTANT;
        std::vector<AlertConditionData> conditions;
        int status = HSM_SENSOR_STATUS_OK; // SensorStatus; managed AlertAction default is Ok
        hsm_alert_destination_mode_t destination = HSM_ALERT_DESTINATION_FROM_PARENT;
        bool has_template = false;
        std::string template_text;
        bool has_icon = false;
        std::string icon; // raw UTF-8; EscapeJsonWire turns non-ASCII into \uXXXX like System.Text.Json
        bool is_disabled = false;
        bool has_confirmation = false;
        int64_t confirmation_ms = 0;
        bool has_scheduled_time = false;
        int64_t scheduled_time_unix_ms = 0;
        bool has_repeat = false;
        hsm_alert_repeat_mode_t repeat = HSM_ALERT_REPEAT_HOURLY;
        bool has_instant_send = false;
        bool instant_send = false;
        bool has_inactivity = false;
        int64_t inactivity_ms = 0;
    };

    // Managed AlertIcon.ToUtf8() (IconExtensions.cs). Returns the raw UTF-8 emoji bytes; the wire
    // serializer escapes non-ASCII to \uXXXX (astral icons become UTF-16 surrogate pairs).
    std::string AlertIconUtf8(hsm_alert_icon_t icon)
    {
        switch (icon)
        {
        case HSM_ALERT_ICON_OK:
            return "\xE2\x9C\x85"; // U+2705 white heavy check mark
        case HSM_ALERT_ICON_WARNING:
            return "\xE2\x9A\xA0"; // U+26A0 warning sign
        case HSM_ALERT_ICON_ERROR:
            return "\xE2\x9D\x8C"; // U+274C cross mark
        case HSM_ALERT_ICON_PAUSE:
            return "\xE2\x8F\xB8"; // U+23F8 pause
        case HSM_ALERT_ICON_ARROW_UP:
            return "\xE2\xAC\x86"; // U+2B06 upwards arrow
        case HSM_ALERT_ICON_ARROW_DOWN:
            return "\xE2\xAC\x87"; // U+2B07 downwards arrow
        case HSM_ALERT_ICON_CLOCK:
            return "\xF0\x9F\x95\x90"; // U+1F550 clock face one oclock
        case HSM_ALERT_ICON_HOURGLASS:
            return "\xE2\x8C\x9B"; // U+231B hourglass
        default:
            return std::string();
        }
    }

    // Serialize one alert exactly as System.Text.Json renders AlertUpdateRequest (declaration
    // order; numeric enums; default encoder via EscapeJsonWire; ConfirmationPeriod is ticks =
    // ms*10000; ScheduledNotificationTime is ISO-8601-Z). The SAME text is embedded by both the
    // internal corpus registration and the wire registration, so the two never drift.
    std::string BuildAlertJson(const AlertData& alert)
    {
        std::ostringstream json;
        json << "{\"Conditions\":[";
        for (size_t i = 0; i < alert.conditions.size(); ++i)
        {
            const auto& cond = alert.conditions[i];
            if (i > 0)
                json << ",";
            json << "{\"Combination\":" << static_cast<int>(cond.combination)
                 << ",\"Operation\":" << static_cast<int>(cond.operation)
                 << ",\"Property\":" << static_cast<int>(cond.property)
                 << ",\"Target\":{\"Type\":" << static_cast<int>(cond.target_type)
                 << ",\"Value\":" << (cond.target_is_null ? std::string("null") : ("\"" + EscapeJsonWire(cond.target_value) + "\""))
                 << "}}";
        }
        json << "]"
             << ",\"Status\":" << alert.status
             << ",\"DestinationMode\":" << static_cast<int>(alert.destination)
             << ",\"Template\":" << (alert.has_template ? ("\"" + EscapeJsonWire(alert.template_text) + "\"") : "null")
             << ",\"Icon\":" << (alert.has_icon ? ("\"" + EscapeJsonWire(alert.icon) + "\"") : "null")
             << ",\"IsDisabled\":" << (alert.is_disabled ? "true" : "false")
             << ",\"ConfirmationPeriod\":" << (alert.has_confirmation ? std::to_string(alert.confirmation_ms * 10000) : "null")
             << ",\"ScheduledNotificationTime\":" << (alert.has_scheduled_time ? ("\"" + IsoUtcFromUnixMs(alert.scheduled_time_unix_ms) + "\"") : "null")
             << ",\"ScheduledRepeatMode\":" << (alert.has_repeat ? std::to_string(static_cast<int>(alert.repeat)) : "null")
             << ",\"ScheduledInstantSend\":" << (alert.has_instant_send ? (alert.instant_send ? "true" : "false") : "null")
             << "}";
        return json.str();
    }

    // "[alert,alert,...]" or "null" when empty — matches a null vs populated List on the wire.
    std::string BuildAlertArrayJson(const std::vector<AlertData>& alerts)
    {
        if (alerts.empty())
            return "null";

        std::string text = "[";
        for (size_t i = 0; i < alerts.size(); ++i)
        {
            if (i > 0)
                text += ",";
            text += BuildAlertJson(alerts[i]);
        }
        text += "]";
        return text;
    }

    // Portable registration-option subset mirroring the managed sensor options
    // (registration_contract.hsmtest). has_description=false => "Description":null —
    // managed instant creates default to "", every other sensor family defaults to null.
    // Tri-state for a nullable bool wire field: Unset => null, else true/false. Fixed underlying
    // type so casting an untrusted ABI int (any value) is well-defined (-fsanitize=enum safe).
    enum class TriBool : int32_t
    {
        Unset = -1,
        False = 0,
        True = 1
    };

    // Mirrors the managed SensorLocation: where a non-computer sensor's path is anchored
    // (CalculateSystemPath). Module => ComputerName/Module/Path; Product => Path (root).
    enum class SensorLocation : int32_t
    {
        Module = 0,
        Product = 1
    };

    struct RegistrationOptions
    {
        int64_t ttl_ms = 0;
        // Raw TTL ticks override (for sentinels like TimeSpan.MaxValue = Int64.MaxValue "never",
        // which cannot be expressed as whole milliseconds). Takes precedence over ttl_ms.
        bool has_ttl_ticks = false;
        int64_t ttl_ticks = 0;
        int32_t unit = -1;
        bool has_description = false;
        std::string description;
        bool has_enum_options = false;
        std::vector<EnumOptionData> enum_options;
        std::vector<AlertData> alerts;     // AddOrUpdate.Alerts (instant/bar data alerts)
        std::vector<AlertData> ttl_alerts; // AddOrUpdate.TtlAlerts (IfInactivityPeriodIs)

        // Generic SensorOptions surface (#1098 §6). has_*/Unset distinguish "emit null" from a value.
        bool has_keep_history = false;
        int64_t keep_history_ms = 0; // -> KeepHistory ticks
        bool has_self_destroy = false;
        int64_t self_destroy_ms = 0; // -> SelfDestroy ticks
        // Statistics and DisplayUnit are emitted by EVERY managed registration, not as null:
        // SensorOptions.Statistics is a non-nullable StatisticsOptions (default None=0), and the
        // typed-DisplayUnit ToApi overload renders Convert.ToInt32((TDisplayUnit?)null) == 0. So the
        // universal default here is 0 (not null). Bar/enum/service sensors override DisplayUnit to
        // null (their EnumSensorOptions/BarSensorOptions ToApi overloads emit null).
        bool has_display_unit = true;
        int32_t display_unit = 0;
        bool has_statistics = true;
        int32_t statistics = 0; // StatisticsOptions flags (EMA=1)
        TriBool aggregate_data = TriBool::Unset;
        TriBool enable_grafana = TriBool::Unset;
        // IsSingletonSensor on the wire is `singleton | is_computer_sensor` (ApiConverters): a computer
        // sensor always registers as a singleton. is_computer_sensor also drives CalculateSystemPath.
        TriBool is_singleton = TriBool::Unset;
        bool is_computer_sensor = false;
        SensorLocation sensor_location = SensorLocation::Module;
    };

    // Prototype merge (DefaultPrototype.Merge): the identity fields (IsComputerSensor / SensorLocation
    // — and at the collector level Path / Type / ComputerName / Module) are PINNED by the prototype
    // and cannot be overridden; every metadata field takes the custom value when the caller set it,
    // else falls back to the prototype default (C# `custom?.X ?? prototype.X`). Returns the merged
    // options the sensor registers with.
    RegistrationOptions MergeRegistrationOptions(const RegistrationOptions& prototype, const RegistrationOptions& custom)
    {
        RegistrationOptions merged = prototype; // identity fields stay pinned to the prototype

        if (custom.ttl_ms > 0 || custom.has_ttl_ticks)
        {
            merged.ttl_ms = custom.ttl_ms;
            merged.has_ttl_ticks = custom.has_ttl_ticks;
            merged.ttl_ticks = custom.ttl_ticks;
        }
        if (custom.unit >= 0)
            merged.unit = custom.unit;
        if (custom.has_description)
        {
            merged.has_description = true;
            merged.description = custom.description;
        }
        if (custom.has_keep_history)
        {
            merged.has_keep_history = true;
            merged.keep_history_ms = custom.keep_history_ms;
        }
        if (custom.has_self_destroy)
        {
            merged.has_self_destroy = true;
            merged.self_destroy_ms = custom.self_destroy_ms;
        }
        if (custom.has_display_unit)
        {
            merged.has_display_unit = true;
            merged.display_unit = custom.display_unit;
        }
        if (custom.has_statistics)
        {
            merged.has_statistics = true;
            merged.statistics = custom.statistics;
        }
        if (custom.aggregate_data != TriBool::Unset)
            merged.aggregate_data = custom.aggregate_data;
        if (custom.enable_grafana != TriBool::Unset)
            merged.enable_grafana = custom.enable_grafana;
        if (custom.is_singleton != TriBool::Unset)
            merged.is_singleton = custom.is_singleton;
        if (!custom.alerts.empty())
            merged.alerts = custom.alerts;
        if (!custom.ttl_alerts.empty())
            merged.ttl_alerts = custom.ttl_alerts;

        return merged;
    }

    // IsSingletonSensor wire value = options.IsSingletonSensor | options.IsComputerSensor (nullable
    // bool OR: null|true=true, null|false=null). Returns "true"/"false"/"null".
    std::string SingletonWireText(const RegistrationOptions& options)
    {
        if (options.is_computer_sensor)
            return "true";
        if (options.is_singleton == TriBool::Unset)
            return "null";
        return options.is_singleton == TriBool::True ? "true" : "false";
    }

    std::string TriBoolWireText(TriBool value)
    {
        if (value == TriBool::Unset)
            return "null";
        return value == TriBool::True ? "true" : "false";
    }

    RegistrationOptions InstantRegistrationDefaults()
    {
        RegistrationOptions options;
        options.has_description = true;
        return options;
    }

    // Enum sensors register through EnumSensorOptions, whose ToApi overload emits DisplayUnit=null
    // (unlike the typed-instant overload's 0). Same instant Description default ("").
    RegistrationOptions EnumRegistrationDefaults()
    {
        RegistrationOptions options = InstantRegistrationDefaults();
        options.has_display_unit = false;
        return options;
    }

    // AddOrUpdate.TTLs (ticks). A TTL alert overrides the plain TTL: when ttl_alerts exist the list
    // is each alert's inactivity ticks (null where unset), mirroring ApiConverters
    // (options.TtlAlerts?.Select(a => a.TtlValue?.Ticks) ?? options.TTLs). Else the plain TTL.
    std::string BuildTtlsText(const RegistrationOptions& options)
    {
        if (options.has_ttl_ticks)
            return "[" + std::to_string(options.ttl_ticks) + "]";

        if (!options.ttl_alerts.empty())
        {
            std::string text = "[";
            for (size_t i = 0; i < options.ttl_alerts.size(); ++i)
            {
                if (i > 0)
                    text += ",";
                const auto& alert = options.ttl_alerts[i];
                text += alert.has_inactivity ? std::to_string(alert.inactivity_ms * 10000) : "null";
            }
            text += "]";
            return text;
        }

        if (options.ttl_ms > 0)
            return "[" + std::to_string(options.ttl_ms * 10000) + "]";

        return "null";
    }

    // Canonical cross-language registration text — must stay byte-identical to the C#
    // harness RegistrationText (fixed field order; TTL in .NET ticks = ms * 10000).
    std::string BuildRegistrationJson(const std::string& path, hsm_sensor_type_t type, const RegistrationOptions& options)
    {
        const std::string ttl_text = BuildTtlsText(options);

        std::string enums_text = "null";
        if (options.has_enum_options)
        {
            enums_text = "[";
            for (size_t i = 0; i < options.enum_options.size(); ++i)
            {
                const auto& option = options.enum_options[i];
                if (i > 0)
                    enums_text += ",";
                enums_text += "{\"Key\":" + std::to_string(option.key) +
                              ",\"Value\":\"" + EscapeJson(option.value) +
                              "\",\"Color\":" + std::to_string(option.color) +
                              ",\"Description\":\"" + EscapeJson(option.description) + "\"}";
            }
            enums_text += "]";
        }

        return "{\"Command\":\"AddOrUpdate\",\"Path\":\"" + EscapeJson(path) +
               "\",\"SensorType\":" + std::to_string(static_cast<int>(type)) +
               ",\"TTLTicks\":" + ttl_text +
               ",\"OriginalUnit\":" + (options.unit >= 0 ? std::to_string(options.unit) : "null") +
               ",\"Description\":" + (options.has_description ? "\"" + EscapeJson(options.description) + "\"" : "null") +
               ",\"EnumOptions\":" + enums_text +
               ",\"Alerts\":" + BuildAlertArrayJson(options.alerts) +
               ",\"TtlAlerts\":" + BuildAlertArrayJson(options.ttl_alerts) +
               ",\"KeepHistory\":" + (options.has_keep_history ? std::to_string(options.keep_history_ms * 10000) : "null") +
               ",\"SelfDestroy\":" + (options.has_self_destroy ? std::to_string(options.self_destroy_ms * 10000) : "null") +
               ",\"Statistics\":" + (options.has_statistics ? std::to_string(options.statistics) : "null") +
               ",\"DisplayUnit\":" + (options.has_display_unit ? std::to_string(options.display_unit) : "null") +
               ",\"IsSingletonSensor\":" + SingletonWireText(options) +
               ",\"AggregateData\":" + TriBoolWireText(options.aggregate_data) +
               ",\"EnableGrafana\":" + TriBoolWireText(options.enable_grafana) + "}";
    }

    // Real wire JSON for AddOrUpdateSensorRequest (#1096 §15) — byte-identical to net8
    // System.Text.Json. Most fields the native does not model are emitted as their .NET defaults
    // (null / 0 / false). The obsolete TtlAlert/TTL serialize as null (get => null). EnumOption
    // wire order is Key, Value, Description, Color (NOTE: differs from the internal registration
    // text's Key,Value,Color,Description). The Command discriminator is Type:0.
    std::string BuildWireRegistrationJson(const std::string& path, hsm_sensor_type_t type, const RegistrationOptions& options)
    {
        const std::string ttls = BuildTtlsText(options);

        std::string enums = "null";
        if (options.has_enum_options)
        {
            enums = "[";
            for (size_t i = 0; i < options.enum_options.size(); ++i)
            {
                const auto& option = options.enum_options[i];
                if (i > 0)
                    enums += ",";
                enums += "{\"Key\":" + std::to_string(option.key) +
                         ",\"Value\":\"" + EscapeJsonWire(option.value) +
                         "\",\"Description\":\"" + EscapeJsonWire(option.description) +
                         "\",\"Color\":" + std::to_string(option.color) + "}";
            }
            enums += "]";
        }

        std::ostringstream json;
        json << "{\"Type\":0"
             << ",\"Alerts\":" << BuildAlertArrayJson(options.alerts)
             << ",\"TtlAlerts\":" << BuildAlertArrayJson(options.ttl_alerts)
             << ",\"TtlAlert\":null"
             << ",\"SensorType\":" << static_cast<int>(type)
             << ",\"Description\":" << (options.has_description ? ("\"" + EscapeJsonWire(options.description) + "\"") : "null")
             << ",\"DefaultChats\":null"
             << ",\"KeepHistory\":" << (options.has_keep_history ? std::to_string(options.keep_history_ms * 10000) : "null")
             << ",\"SelfDestroy\":" << (options.has_self_destroy ? std::to_string(options.self_destroy_ms * 10000) : "null")
             << ",\"TTLs\":" << ttls
             << ",\"TTL\":null"
             << ",\"Statistics\":" << (options.has_statistics ? std::to_string(options.statistics) : "null")
             << ",\"IsSingletonSensor\":" << SingletonWireText(options)
             << ",\"AggregateData\":" << TriBoolWireText(options.aggregate_data)
             << ",\"EnableGrafana\":" << TriBoolWireText(options.enable_grafana)
             << ",\"OriginalUnit\":" << (options.unit >= 0 ? std::to_string(options.unit) : "null")
             << ",\"DisplayUnit\":" << (options.has_display_unit ? std::to_string(options.display_unit) : "null")
             << ",\"DefaultAlertsOptions\":0"
             << ",\"IsForceUpdate\":false"
             << ",\"EnumOptions\":" << enums
             << ",\"Key\":null"
             << ",\"Path\":\"" << EscapeJsonWire(path) << "\"}";
        return json.str();
    }

    // C# Math.Round(value, precision, MidpointRounding.AwayFromZero) — std::round is
    // half-away-from-zero, matching the double-bar field rounding contract.
    double RoundAwayFromZero(double value, int precision)
    {
        const double scale = std::pow(10.0, precision);
        return std::round(value * scale) / scale;
    }

    // Transcription of the C# MonitoringBarBase<T> aggregation state (MonitoringBar.cs).
    // Values are accumulated as doubles for both bar flavors: an int bar's running sum in C#
    // is a double too, so a 1e10 total stays exact and the mean math is bit-identical.
    struct MonitoringBar
    {
        bool is_int = false;
        int precision = 2;
        int64_t period_ms = 1;
        int64_t open_ms = 0;
        int64_t close_ms = 0;
        double total_sum = 0.0;
        double min = 0.0;
        double max = 0.0;
        double first = 0.0;
        double last = 0.0;
        int32_t count = 0;

        void Init(int64_t now_ms)
        {
            open_ms = now_ms / period_ms * period_ms;
            close_ms = open_ms + period_ms;
            total_sum = 0.0;
            min = 0.0;
            max = 0.0;
            first = 0.0;
            last = 0.0;
            count = 0;
        }

        void AddValue(double value)
        {
            if (count == 0)
            {
                first = value;
                min = value;
                max = value;
                total_sum = value;
            }
            else
            {
                total_sum += value;
                min = std::min(min, value);
                max = std::max(max, value);
            }

            last = value;
            ++count;
        }

        void AddPartial(double partial_min, double partial_max, double partial_mean, double partial_first, double partial_last, int32_t partial_count)
        {
            if (partial_count < 1)
                return;

            if (count == 0)
            {
                first = partial_first;
                min = partial_min;
                max = partial_max;
            }
            else
            {
                min = std::min(min, partial_min);
                max = std::max(max, partial_max);
            }

            total_sum += partial_mean * partial_count;
            last = partial_last;
            count += partial_count;
        }
    };

    // Strict inclusive validation for int partials (PublicBarMonitoringSensor.IsValidPartial).
    bool IsValidIntPartial(int32_t min, int32_t max, int32_t mean, int32_t first, int32_t last, int32_t count)
    {
        return count >= 1 &&
               min <= max &&
               mean >= min && mean <= max &&
               first >= min && first <= max &&
               last >= min && last <= max;
    }

    // FP-tolerant validation for double partials (DoubleBarPublicSensor.IsValidPartial):
    // tolerance = max(1e-12, |max - min| * 1e-9).
    bool IsValidDoublePartial(double min, double max, double mean, double first, double last, int32_t count)
    {
        if (count < 1)
            return false;

        if (!std::isfinite(min) || !std::isfinite(max) || !std::isfinite(mean) ||
            !std::isfinite(first) || !std::isfinite(last))
            return false;

        const double tolerance = std::max(1e-12, std::abs(max - min) * 1e-9);

        return min <= max + tolerance &&
               mean >= min - tolerance && mean <= max + tolerance &&
               first >= min - tolerance && first <= max + tolerance &&
               last >= min - tolerance && last <= max + tolerance;
    }

    std::string BarIntFieldJson(double value)
    {
        return std::to_string(static_cast<long long>(value));
    }

    // Canonical cross-language bar payload — field order and shape mirror the C# harness's
    // BarPayloadText. Numeric asserts compare with tolerance, so double formatting may differ.
    std::string MonitoringBarJson(const MonitoringBar& bar, const std::string& path)
    {
        const double raw_mean = bar.total_sum / bar.count;

        std::string min_text, max_text, mean_text, first_text, last_text;

        if (bar.is_int)
        {
            // C# int bar mean is (int)Math.Round(sum / count) — round-half-to-EVEN.
            // std::nearbyint matches under the default FE_TONEAREST mode; std::round does not.
            min_text = BarIntFieldJson(bar.min);
            max_text = BarIntFieldJson(bar.max);
            mean_text = std::to_string(static_cast<int32_t>(std::nearbyint(raw_mean)));
            first_text = BarIntFieldJson(bar.first);
            last_text = BarIntFieldJson(bar.last);
        }
        else
        {
            min_text = DoubleJson(RoundAwayFromZero(bar.min, bar.precision));
            max_text = DoubleJson(RoundAwayFromZero(bar.max, bar.precision));
            mean_text = DoubleJson(RoundAwayFromZero(raw_mean, bar.precision));
            first_text = DoubleJson(RoundAwayFromZero(bar.first, bar.precision));
            last_text = DoubleJson(RoundAwayFromZero(bar.last, bar.precision));
        }

        std::ostringstream json;
        json << "{"
             << "\"Type\":" << (bar.is_int ? static_cast<int>(HSM_SENSOR_TYPE_INT_BAR) : static_cast<int>(HSM_SENSOR_TYPE_DOUBLE_BAR)) << ","
             << "\"Path\":\"" << EscapeJson(path) << "\","
             << "\"Min\":" << min_text << ","
             << "\"Max\":" << max_text << ","
             << "\"Mean\":" << mean_text << ","
             << "\"First\":" << first_text << ","
             << "\"Last\":" << last_text << ","
             << "\"Count\":" << bar.count << ","
             << "\"OpenTimeMs\":" << bar.open_ms << ","
             << "\"CloseTimeMs\":" << bar.close_ms << ","
             << "\"Status\":" << static_cast<int>(HSM_SENSOR_STATUS_OK) << ","
             << "\"Comment\":\"\""
             << "}";

        return json.str();
    }

    // Real wire JSON for a bar DTO (#1096 §15): Type, Min, Max, Mean, FirstValue, LastValue,
    // Percentiles(null), OpenTime, CloseTime, Count, Comment(null), Time, Status, Key, Path.
    // Field VALUES reuse the same int/double formatting as the internal MonitoringBarJson.
    std::string BuildWireBarJson(const MonitoringBar& bar, int64_t time_ms, const std::string& path)
    {
        const double raw_mean = bar.total_sum / bar.count;

        std::string min_text, max_text, mean_text, first_text, last_text;
        if (bar.is_int)
        {
            min_text = BarIntFieldJson(bar.min);
            max_text = BarIntFieldJson(bar.max);
            mean_text = std::to_string(static_cast<int32_t>(std::nearbyint(raw_mean)));
            first_text = BarIntFieldJson(bar.first);
            last_text = BarIntFieldJson(bar.last);
        }
        else
        {
            min_text = DoubleJson(RoundAwayFromZero(bar.min, bar.precision));
            max_text = DoubleJson(RoundAwayFromZero(bar.max, bar.precision));
            mean_text = DoubleJson(RoundAwayFromZero(raw_mean, bar.precision));
            first_text = DoubleJson(RoundAwayFromZero(bar.first, bar.precision));
            last_text = DoubleJson(RoundAwayFromZero(bar.last, bar.precision));
        }

        std::ostringstream json;
        json << "{\"Type\":"
             << (bar.is_int ? static_cast<int>(HSM_SENSOR_TYPE_INT_BAR) : static_cast<int>(HSM_SENSOR_TYPE_DOUBLE_BAR))
             << ",\"Min\":" << min_text
             << ",\"Max\":" << max_text
             << ",\"Mean\":" << mean_text
             << ",\"FirstValue\":" << first_text
             << ",\"LastValue\":" << last_text
             << ",\"Percentiles\":null"
             << ",\"OpenTime\":\"" << IsoUtcFromUnixMs(bar.open_ms) << "\""
             << ",\"CloseTime\":\"" << IsoUtcFromUnixMs(bar.close_ms) << "\""
             << ",\"Count\":" << bar.count
             << ",\"Comment\":null"
             << ",\"Time\":\"" << IsoUtcFromUnixMs(time_ms) << "\""
             << ",\"Status\":" << static_cast<int>(HSM_SENSOR_STATUS_OK)
             << ",\"Key\":null"
             << ",\"Path\":\"" << EscapeJsonWire(path) << "\"}";
        return json.str();
    }

    std::string JoinPathParts(std::initializer_list<std::string> parts)
    {
        std::string result;

        for (const auto& part : parts)
        {
            const auto normalized = TrimSlashes(part);
            if (normalized.empty())
                continue;

            size_t start = 0;
            while (start < normalized.size())
            {
                const auto separator = normalized.find('/', start);
                const auto end = separator == std::string::npos ? normalized.size() : separator;

                if (end > start)
                {
                    const auto segment = normalized.substr(start, end - start);
                    if (!IsBlank(segment))
                    {
                        if (!result.empty())
                            result += "/";

                        result += segment;
                    }
                }

                if (separator == std::string::npos)
                    break;

                start = separator + 1;
            }
        }

        return result;
    }

    struct SensorSnapshot
    {
        std::string path;
        hsm_sensor_type_t type;
        std::string value_json;
        hsm_sensor_status_t status;
        std::string comment;
    };

    class NativeCollector;

    class NativeSensor
    {
    public:
        NativeSensor(
            std::weak_ptr<NativeCollector> collector,
            std::string path,
            hsm_sensor_type_t type,
            bool is_last_value,
            std::string default_value_json)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(type),
              is_last_value_(is_last_value),
              last_value_json_(std::move(default_value_json))
        {
        }

        // Bar sensor constructor: the bar opens immediately, aligned to bar_period_ms.
        // Values accumulate regardless of collector state (matching C#: only the publish
        // is gated on Running, not the accumulation).
        NativeSensor(
            std::weak_ptr<NativeCollector> collector,
            std::string path,
            hsm_sensor_type_t type,
            int64_t bar_period_ms,
            int32_t precision)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(type),
              is_last_value_(false),
              is_bar_(true)
        {
            bar_.is_int = type == HSM_SENSOR_TYPE_INT_BAR;
            bar_.precision = static_cast<int>(precision);
            bar_.period_ms = bar_period_ms;
            bar_.Init(UnixTimeMilliseconds());
        }

        // Periodic sensor constructor (rate / function / values-function). Both function
        // pointers null => rate sensor. The first post is due immediately; collector Start
        // re-arms via ResetPeriodicBaseline so restarts also post at once.
        NativeSensor(
            std::weak_ptr<NativeCollector> collector,
            std::string path,
            hsm_sensor_type_t type,
            int64_t post_period_ms,
            hsm_int_function_t int_function,
            hsm_int_values_function_t int_values_function,
            void* function_user_data,
            int32_t max_cache_size)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(type),
              is_last_value_(false),
              is_periodic_(true),
              post_period_ms_(post_period_ms),
              next_post_ms_(SteadyMilliseconds()),
              int_function_(int_function),
              int_values_function_(int_values_function),
              function_user_data_(function_user_data),
              function_max_cache_(max_cache_size)
        {
            // Seed the scheduler hint so a sensor created while the collector is already
            // running is due immediately (managed: dynamic create posts at once); pre-start
            // sensors are re-anchored by ResetPeriodicBaseline on Start.
            next_post_hint_.store(next_post_ms_);
        }

        // File sensor constructor (string-content publishing only).
        NativeSensor(
            std::weak_ptr<NativeCollector> collector,
            std::string path,
            std::string default_file_name,
            std::string extension)
            : collector_(std::move(collector)),
              path_(std::move(path)),
              type_(HSM_SENSOR_TYPE_FILE),
              is_last_value_(false),
              file_name_(std::move(default_file_name)),
              file_extension_(std::move(extension))
        {
        }

        hsm_result_t AddInt(int32_t value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddBool(bool value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddDouble(double value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddString(const char* value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddEnum(int32_t value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddTimeSpan(int64_t ticks, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddVersion(int32_t major, int32_t minor, int32_t build, int32_t revision, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddBarInt(int32_t value);
        hsm_result_t AddBarDouble(double value);
        hsm_result_t AddBarIntPartial(int32_t min, int32_t max, int32_t mean, int32_t first, int32_t last, int32_t count);
        hsm_result_t AddBarDoublePartial(double min, double max, double mean, double first, double last, int32_t count);
        hsm_result_t AddRate(double value, hsm_sensor_status_t status, const char* comment);
        hsm_result_t AddFunctionInt(int32_t value);
        hsm_result_t AddFile(const char* utf8_content, hsm_sensor_status_t status, const char* comment);
        bool TryGetLastValueSnapshot(SensorSnapshot& snapshot) const;
        bool TryFlushBarJson(std::string& out_json);
        void ResetPeriodicBaseline();
        bool TryBuildPeriodicJson(std::string& out_json);
        bool IsPeriodic() const;
        bool MatchesPeriodicShape(hsm_int_function_t int_function, hsm_int_values_function_t int_values_function) const;
        hsm_sensor_type_t Type() const;
        bool IsLastValue() const;

        // Immutable after creation: set under the collector lock before the sensor is
        // published into the registry, so it is safe to read under the collector lock
        // without taking the sensor lock (the lock order stays one-way).
        void SetRegistrationJson(std::string json) { registration_json_ = std::move(json); }
        const std::string& RegistrationJson() const { return registration_json_; }

        // Store the registration inputs (full sensor path + options) and build the internal
        // registration text. Keeping the inputs lets AttachAlert rebuild the payload in place.
        void SetRegistration(std::string sensor_path, hsm_sensor_type_t type, RegistrationOptions options)
        {
            registration_path_ = std::move(sensor_path);
            registration_type_ = type;
            registration_options_ = std::move(options);
            registration_json_ = BuildRegistrationJson(registration_path_, registration_type_, registration_options_);
        }

        // Attach a built alert and rebuild the registration. Must run before the registration is
        // emitted (pre-Start, or pre-create-while-running). A TTL alert (IfInactivityPeriodIs) goes
        // to TtlAlerts and drives TTLs; every other alert goes to Alerts.
        hsm_result_t AttachAlert(const AlertData& alert)
        {
            if (alert.kind == HSM_ALERT_KIND_TTL)
                registration_options_.ttl_alerts.push_back(alert);
            else
                registration_options_.alerts.push_back(alert);

            registration_json_ = BuildRegistrationJson(registration_path_, registration_type_, registration_options_);
            return HSM_RESULT_OK;
        }

        // Real wire (System.Text.Json) registration payload, built on demand from the stored inputs.
        std::string WireRegistrationJson() const
        {
            return BuildWireRegistrationJson(registration_path_, registration_type_, registration_options_);
        }

        // Periodic scheduling clock (issue #1095 §13). Set under the collector lock at
        // registration; the periodic due-time and rate elapsed read through it so a test
        // clock can drive cadence. Null falls back to the real monotonic clock.
        void SetClock(std::shared_ptr<Clock> clock) { clock_ = std::move(clock); }

        // Lock-free hint of the next periodic due time (steady ms) for the scheduler's wait
        // computation; std::numeric_limits<int64_t>::max() for a non-periodic sensor.
        int64_t NextPostHint() const { return next_post_hint_.load(); }

    private:
        hsm_result_t AddValueJson(std::string value_json, hsm_sensor_status_t status, const char* comment);

        int64_t SteadyNowMs() const { return clock_ ? clock_->SteadyNowMs() : SteadyMilliseconds(); }

        template <typename Accumulate>
        hsm_result_t AccumulateBar(Accumulate&& accumulate);

        std::weak_ptr<NativeCollector> collector_;
        std::string path_;
        hsm_sensor_type_t type_;
        bool is_last_value_;
        bool is_bar_ = false;
        mutable std::mutex mutex_;
        std::string last_value_json_;
        hsm_sensor_status_t last_status_ = HSM_SENSOR_STATUS_OFF_TIME;
        std::string last_comment_;
        MonitoringBar bar_;

        // Periodic scheduling clock (real by default) + lock-free next-due hint read by the
        // scheduler without taking the sensor lock.
        std::shared_ptr<Clock> clock_;
        std::atomic<int64_t> next_post_hint_{ (std::numeric_limits<int64_t>::max)() };

        // Periodic (rate / function) state, guarded by mutex_.
        bool is_periodic_ = false;
        int64_t post_period_ms_ = 0;
        int64_t next_post_ms_ = 0;
        double rate_sum_ = 0.0;
        hsm_sensor_status_t rate_status_ = HSM_SENSOR_STATUS_OK;
        std::string rate_comment_;
        int64_t rate_prev_ms_ = 0;
        bool rate_has_prev_ = false;
        hsm_int_function_t int_function_ = nullptr;
        hsm_int_values_function_t int_values_function_ = nullptr;
        void* function_user_data_ = nullptr;
        std::deque<int32_t> function_values_;
        int32_t function_max_cache_ = 0;

        // File sensor identity.
        std::string file_name_;
        std::string file_extension_;

        std::string registration_json_;
        std::string registration_path_;
        hsm_sensor_type_t registration_type_ = HSM_SENSOR_TYPE_INT;
        RegistrationOptions registration_options_;
    };

    class NativeCollector : public std::enable_shared_from_this<NativeCollector>
    {
    public:
        // 0 selects the managed CollectorOptions default for every numeric field
        // EXCEPT the dedup window, where 0 is a meaningful value (log immediately,
        // no dedup) and is passed through verbatim. Negative fields are rejected
        // before construction (hsm_collector_create validation).
        explicit NativeCollector(const hsm_collector_options_t& options)
            : access_key_(CopyString(options.access_key)),
              server_address_(CopyString(options.server_address)),
              port_(options.port),
              client_name_(CopyString(options.client_name)),
              module_(CopyString(options.module)),
              computer_name_(CopyString(options.computer_name)),
              max_queue_size_(options.max_queue_size > 0 ? options.max_queue_size : 20000),
              max_values_in_package_(options.max_values_in_package > 0 ? options.max_values_in_package : 1000),
              collect_period_ms_(options.package_collect_period_ms > 0 ? options.package_collect_period_ms : 15000),
              request_timeout_ms_(options.request_timeout_ms > 0 ? options.request_timeout_ms : 30000),
              max_sensors_(options.max_sensors > 0 ? options.max_sensors : 100000),
              allow_untrusted_certificate_(options.allow_untrusted_server_certificate),
              allow_plaintext_transport_(options.allow_plaintext_transport),
              dedup_window_ms_(options.exception_deduplicator_window_ms),
              max_deduplicated_messages_(options.max_deduplicated_messages > 0 ? options.max_deduplicated_messages : 1000)
        {
            // The scheduler reads time through clock_ (swappable before Start via SetClock) and
            // wakes at the earliest periodic due-time. Lock order is task -> collector: BOTH the
            // now and due callbacks lock the collector mutex (never the reverse), so clock_ is
            // never read unlocked even if a caller swaps it.
            scheduler_task_ = std::make_unique<ScheduledTask>(
                [this] {
                    std::lock_guard<std::mutex> guard(mutex_);
                    return clock_->SteadyNowMs();
                },
                [this] { return EarliestNextPostMs(); },
                [this] { TickPeriodicSensors(); });

            // Send seam default: record batches into sent_values_ (the behavior corpus and the
            // sent_count/get_sent_json accessors observe this). The HTTP build swaps in a real
            // libcurl POST via TestInstallHttpSender before Start.
            sender_ = [this](std::vector<std::string>& batch) { return RecordBatch(batch); };
        }

        // Test-only seam (no public C ABI surface): swap the scheduling clock before Start.
        // Existing sensors are re-pointed at it too, so a manual clock drives both the
        // scheduler wait and the sensors' due-checks.
        void SetClock(std::shared_ptr<Clock> clock)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            clock_ = std::move(clock);
            for (const auto& sensor : sensors_)
                sensor.second->SetClock(clock_);
        }

        void TestInstallManualClock(int64_t base_ms)
        {
            SetClock(std::make_shared<ManualClock>(base_ms, base_ms));
        }

#if defined(HSM_COLLECTOR_HTTP)
        // Live-path seam (HTTP build): swap the recording sender for a real libcurl POST to the
        // batch /list route. Install before Start, same one-way contract as SetClock — the worker
        // thread only reads sender_ after Start, so the swap needs no lock. One transport per
        // collector; Cancel()/ResetCancel() are wired into Stop/StartWorker so a Stop aborts an
        // in-flight POST (the CancelPendingRequests primitive) and a restart re-arms it.
        void TestInstallHttpSender()
        {
            http_transport_ = std::make_unique<hsm::http::HttpTransport>(
                request_timeout_ms_, /*verify_peer=*/!allow_untrusted_certificate_);
            endpoints_ = hsm::http::MakeEndpoints(server_address_, port_, allow_plaintext_transport_);
            sender_ = [this](std::vector<std::string>& batch) { return HttpSendBatch(batch); };
        }
#endif

        // Advance the installed manual clock and wake the scheduler so it re-evaluates the
        // virtual due-time at once (no real-time wait).
        void TestAdvanceClock(int64_t delta_ms)
        {
            std::shared_ptr<Clock> clock;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                clock = clock_;
            }

            if (auto* manual = dynamic_cast<ManualClock*>(clock.get()))
                manual->AdvanceMs(delta_ms);

            scheduler_task_->Wake();
        }

        // Drive the error-routing/dedup path directly (the production routes — validation
        // drops, shutdown discards — are hard to fire deterministically from a unit test).
        void TestLogError(const std::string& message)
        {
            LogError(message);
        }

        ~NativeCollector()
        {
            // Terminal teardown (destroy without an explicit dispose/stop): only the
            // threads are reclaimed — there is no observable place left to drain into.
            // Idempotent with Dispose() via the joinable() guards.
            StopScheduler();
            StopWorker();
        }

        // Start: Stopped -> Starting -> Running. Idempotent (Running/Starting -> no-op),
        // rejected once Disposed. The op lock serializes the whole transition against
        // Stop/Dispose so listeners observe a consistent order.
        hsm_result_t Start()
        {
            std::lock_guard<std::mutex> op_guard(op_mutex_);

            // The state flip to Starting AND the registration snapshot happen under one lock:
            // otherwise a CreateSensor interleaving between them would be both in this snapshot
            // and self-register via RegisterSensorLocked (Starting qualifies), double-counting
            // its AddOrUpdate. Every start re-registers every sensor (mirrors C# InitAsync).
            // RegistrationJson is immutable, so reading it under the collector lock keeps the
            // one-way lock order. Map iteration order is unspecified — fixtures assert counts.
            std::vector<std::shared_ptr<NativeSensor>> sensors_snapshot;
            {
                std::lock_guard<std::mutex> guard(mutex_);

                if (state_ == CollectorState::Disposed)
                    return SetError(HSM_RESULT_INVALID_STATE, "Collector is disposed.");

                if (state_ == CollectorState::Running || state_ == CollectorState::Starting)
                {
                    ClearError();
                    return HSM_RESULT_OK;
                }

                state_ = CollectorState::Starting;
                ClearError();

                sensors_snapshot.reserve(sensors_.size());
                for (const auto& sensor : sensors_)
                {
                    sensors_snapshot.push_back(sensor.second);
                    registrations_.push_back(sensor.second->RegistrationJson());
                }
            }

            NotifyLifecycle(CollectorState::Starting);

            // Re-arm periodic sensors outside the collector lock (sensor locks are never taken
            // under it): the first post fires immediately, and a restarted rate sensor must not
            // divide by the stopped gap (fresh elapsed baseline — mirrors C# InitAsync).
            for (const auto& sensor : sensors_snapshot)
                sensor->ResetPeriodicBaseline();

            StartWorker();
            StartScheduler();

            {
                std::lock_guard<std::mutex> guard(mutex_);
                state_ = CollectorState::Running;
            }

            NotifyLifecycle(CollectorState::Running);
            return HSM_RESULT_OK;
        }

        // Stop: Running -> Stopping -> Stopped. Idempotent (Stopped/Disposed -> no-op).
        hsm_result_t Stop()
        {
            std::lock_guard<std::mutex> op_guard(op_mutex_);
            return StopCore();
        }

        // Caller holds op_mutex_. Drives the stop transition exactly once (Stopped/Disposed
        // are no-ops), firing the Stopping/Stopped notifications around the flush+drain.
        // Dispose reuses this so a dispose racing an in-flight Stop joins on op_mutex_ and
        // sees Stopped here — exactly one stopped-notification, no duplicate flush.
        hsm_result_t StopCore()
        {
            std::vector<std::shared_ptr<NativeSensor>> sensors_snapshot;
            {
                std::lock_guard<std::mutex> guard(mutex_);

                if (state_ == CollectorState::Stopped || state_ == CollectorState::Disposed)
                {
                    ClearError();
                    return HSM_RESULT_OK;
                }

                state_ = CollectorState::Stopping;
                sensors_snapshot.reserve(sensors_.size());
                for (const auto& sensor : sensors_)
                    sensors_snapshot.push_back(sensor.second);

                ClearError();
            }

            NotifyLifecycle(CollectorState::Stopping);

            // Phase 1: stop the periodic scheduler. Posts racing the state flip are dropped by
            // the CanAcceptData gate; rate sums / function posts are deliberately NOT flushed
            // (a partial-window rate is alert-noise risk — only bars preserve data at stop).
            StopScheduler();

            // Phase 2: flush sensors — last-value snapshots and non-empty partial bars — into
            // the queue, bypassing the data gate (this is the explicit stop-flush path).
            std::vector<std::string> flushed;
            for (const auto& sensor : sensors_snapshot)
            {
                SensorSnapshot snapshot;
                if (sensor->TryGetLastValueSnapshot(snapshot))
                    flushed.push_back(BuildValueJson(snapshot.path, snapshot.type, snapshot.value_json, snapshot.status, snapshot.comment));

                std::string bar_json;
                if (sensor->TryFlushBarJson(bar_json))
                    flushed.push_back(std::move(bar_json));
            }

            for (auto& json : flushed)
                Enqueue(std::move(json));

            // Phase 3: stop the dispatcher and drain everything still queued. A send failure
            // during this bounded flush drops the remainder (the graceful stop must not hang
            // on a dead transport) — same contract as the C# stop flush.
            StopWorker();
            DrainQueueOnStop();

            {
                std::lock_guard<std::mutex> guard(mutex_);
                state_ = CollectorState::Stopped;
            }

            NotifyLifecycle(CollectorState::Stopped);
            return HSM_RESULT_OK;
        }

        // Dispose: terminal, idempotent, never throws. Stops first if active (firing
        // Stopping/Stopped, like the managed Dispose-from-active path), then Disposed.
        void Dispose()
        {
            std::lock_guard<std::mutex> op_guard(op_mutex_);

            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (state_ == CollectorState::Disposed)
                    return;
            }

            StopCore();

            {
                std::lock_guard<std::mutex> guard(mutex_);
                state_ = CollectorState::Disposed;
            }
            // No listener notification for Disposed — mirrors the managed collector, which
            // fires only ToStopping/ToStopped on dispose-from-active and nothing once stopped.
        }

        hsm_collector_status_t Status() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return ToPublicStatus(state_);
        }

        // TestConnection is callable in any state (mirrors ConnectionResult). The in-memory
        // sender is always reachable until the HTTP transport lands (#1096); a disposed
        // collector reports invalid state instead of a phantom healthy connection.
        hsm_result_t TestConnection()
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (state_ == CollectorState::Disposed)
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is disposed.");

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t AddLifecycleListener(hsm_lifecycle_callback_t callback, void* user_data)
        {
            if (callback == nullptr)
                return HSM_RESULT_INVALID_ARGUMENT;

            // Guarded by its own mutex (not op_mutex_): NotifyLifecycle snapshots under this
            // lock and invokes OUTSIDE it, so a callback that registers another listener does
            // not deadlock on a held lock, and the listener vector is never mutated mid-iteration.
            std::lock_guard<std::mutex> guard(listeners_mutex_);
            lifecycle_listeners_.push_back(LifecycleListener{ callback, user_data });
            return HSM_RESULT_OK;
        }

        void SetLogger(hsm_log_callback_t callback, void* user_data)
        {
            std::lock_guard<std::mutex> guard(logger_mutex_);
            log_callback_ = callback;
            log_user_data_ = user_data;
        }

        hsm_result_t CreateSensor(
            const char* path,
            hsm_sensor_type_t type,
            bool is_last_value,
            const std::string& default_value_json,
            std::shared_ptr<NativeSensor>& out_sensor,
            const RegistrationOptions& registration = InstantRegistrationDefaults())
        {
            if (!IsValidPath(path))
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);
            const auto sensor_path = CalculateSystemPath(path, registration);

            // Registration is closed while Stopping/Disposed: reject without crashing the host
            // (returns an inert null handle), mirroring the managed CanRegisterSensors gate.
            if (!CanRegisterSensorsLocked())
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is not accepting sensor registrations.");
            }

            const auto existing = sensors_.find(sensor_path);
            if (existing != sensors_.end())
            {
                // IsPeriodic() closes the cross-family hole: a function-int sensor also has
                // Type == INT / is_last_value == false and must not be returned as an instant
                // int handle (the periodic/file create paths guard symmetrically).
                if (existing->second->Type() != type || existing->second->IsLastValue() != is_last_value || existing->second->IsPeriodic())
                {
                    out_sensor.reset();
                    return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path is already registered with a different type.");
                }

                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            // A registered path returns its existing handle above (idempotent, not counted);
            // a genuinely new sensor is capped at MaxSensors (managed registration cap).
            if (sensors_.size() >= static_cast<size_t>(max_sensors_))
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_LIMIT_EXCEEDED, "MaxSensors limit reached.");
            }

            auto sensor = std::make_shared<NativeSensor>(weak_from_this(), sensor_path, type, is_last_value, default_value_json);
            RegisterSensorLocked(sensor, type, sensor_path, registration);
            sensors_.emplace(sensor_path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t CreateBarSensor(
            const char* path,
            hsm_sensor_type_t type,
            int64_t bar_period_ms,
            int32_t precision,
            std::shared_ptr<NativeSensor>& out_sensor)
        {
            if (!IsValidPath(path))
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);
            const auto sensor_path = BuildSensorPath(path);

            // Registration is closed while Stopping/Disposed: reject without crashing the host
            // (returns an inert null handle), mirroring the managed CanRegisterSensors gate.
            if (!CanRegisterSensorsLocked())
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is not accepting sensor registrations.");
            }

            const auto existing = sensors_.find(sensor_path);
            if (existing != sensors_.end())
            {
                if (existing->second->Type() != type)
                {
                    out_sensor.reset();
                    return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path is already registered with a different type.");
                }

                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            // A registered path returns its existing handle above (idempotent, not counted);
            // a genuinely new sensor is capped at MaxSensors (managed registration cap).
            if (sensors_.size() >= static_cast<size_t>(max_sensors_))
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_LIMIT_EXCEEDED, "MaxSensors limit reached.");
            }

            auto sensor = std::make_shared<NativeSensor>(weak_from_this(), sensor_path, type, bar_period_ms, precision);
            RegistrationOptions bar_registration; // Statistics:0 default; bars emit DisplayUnit:null
            bar_registration.has_display_unit = false;
            RegisterSensorLocked(sensor, type, sensor_path, bar_registration);
            sensors_.emplace(sensor_path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t CreatePeriodicSensor(
            const char* path,
            hsm_sensor_type_t type,
            int64_t post_period_ms,
            hsm_int_function_t int_function,
            hsm_int_values_function_t int_values_function,
            void* function_user_data,
            int32_t max_cache_size,
            std::shared_ptr<NativeSensor>& out_sensor)
        {
            if (!IsValidPath(path))
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);
            const auto sensor_path = BuildSensorPath(path);

            // Registration is closed while Stopping/Disposed: reject without crashing the host
            // (returns an inert null handle), mirroring the managed CanRegisterSensors gate.
            if (!CanRegisterSensorsLocked())
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is not accepting sensor registrations.");
            }

            const auto existing = sensors_.find(sensor_path);
            if (existing != sensors_.end())
            {
                // The callback-shape check closes the intra-periodic INT ambiguity: a no-params
                // function and a values-function both register Type == INT, and returning the
                // other flavor's sensor from create would hand back a type-confused handle.
                if (existing->second->Type() != type ||
                    !existing->second->IsPeriodic() ||
                    !existing->second->MatchesPeriodicShape(int_function, int_values_function))
                {
                    out_sensor.reset();
                    return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path is already registered with a different type.");
                }

                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            // A registered path returns its existing handle above (idempotent, not counted);
            // a genuinely new sensor is capped at MaxSensors (managed registration cap).
            if (sensors_.size() >= static_cast<size_t>(max_sensors_))
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_LIMIT_EXCEEDED, "MaxSensors limit reached.");
            }

            auto sensor = std::make_shared<NativeSensor>(
                weak_from_this(), sensor_path, type, post_period_ms,
                int_function, int_values_function, function_user_data, max_cache_size);
            RegistrationOptions periodic_registration; // Statistics:0, DisplayUnit:0 (member defaults)
            if (type == HSM_SENSOR_TYPE_RATE)
                periodic_registration.unit = 3000; // RateSensorOptions default Unit = ValueInSecond
            RegisterSensorLocked(sensor, type, sensor_path, periodic_registration);
            sensors_.emplace(sensor_path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        hsm_result_t CreateFileSensor(
            const char* path,
            const char* default_file_name,
            const char* extension,
            std::shared_ptr<NativeSensor>& out_sensor)
        {
            if (!IsValidPath(path))
                return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path must not be empty.");

            std::lock_guard<std::mutex> guard(mutex_);
            const auto sensor_path = BuildSensorPath(path);

            // Registration is closed while Stopping/Disposed: reject without crashing the host
            // (returns an inert null handle), mirroring the managed CanRegisterSensors gate.
            if (!CanRegisterSensorsLocked())
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_INVALID_STATE, "Collector is not accepting sensor registrations.");
            }

            const auto existing = sensors_.find(sensor_path);
            if (existing != sensors_.end())
            {
                if (existing->second->Type() != HSM_SENSOR_TYPE_FILE)
                {
                    out_sensor.reset();
                    return SetError(HSM_RESULT_INVALID_ARGUMENT, "Sensor path is already registered with a different type.");
                }

                out_sensor = existing->second;
                ClearError();
                return HSM_RESULT_OK;
            }

            // A registered path returns its existing handle above (idempotent, not counted);
            // a genuinely new sensor is capped at MaxSensors (managed registration cap).
            if (sensors_.size() >= static_cast<size_t>(max_sensors_))
            {
                out_sensor.reset();
                return SetError(HSM_RESULT_LIMIT_EXCEEDED, "MaxSensors limit reached.");
            }

            auto sensor = std::make_shared<NativeSensor>(
                weak_from_this(), sensor_path, CopyString(default_file_name), CopyString(extension));
            RegisterSensorLocked(sensor, HSM_SENSOR_TYPE_FILE, sensor_path, RegistrationOptions{});
            sensors_.emplace(sensor_path, sensor);
            out_sensor = std::move(sensor);

            ClearError();
            return HSM_RESULT_OK;
        }

        // Allocate an alert owned by the collector; the returned pointer is stable for the
        // collector's lifetime and is handed back as the opaque hsm_alert_t handle. Alerts are
        // built single-threaded by the caller before attachment; the lock only guards the vector.
        AlertData* CreateAlertData(hsm_alert_kind_t kind)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            auto alert = std::make_shared<AlertData>();
            alert->kind = kind;
            alert_data_.push_back(alert);
            return alert.get();
        }

        // ServiceCommandsSensor: a string sensor at ".module/Service commands" (the CalculateSystemPath
        // computer/module prefix is applied as for any sensor) with the managed Description and the
        // implicit IfReceivedNewValue -> ThenSendNotification("[$product] $value - $comment") alert.
        hsm_result_t CreateServiceCommandsSensor(std::shared_ptr<NativeSensor>& out_sensor)
        {
            RegistrationOptions registration = InstantRegistrationDefaults();
            registration.description =
                "This is a special sensor that sends information about various critical commands (Start, Stop, "
                "Update, Restart, etc.) and information about the initiator.";
            // Prototype defaults (InstantSensorOptionsPrototype): TTL = TimeSpan.MaxValue ("never") and
            // EnableForGrafana = true. ServiceSensorOptions is an EnumSensorOptions -> DisplayUnit null
            // (Statistics = None(0) is the universal default).
            registration.has_ttl_ticks = true;
            registration.ttl_ticks = (std::numeric_limits<int64_t>::max)();
            registration.enable_grafana = TriBool::True;
            registration.has_display_unit = false;

            AlertData alert;
            alert.kind = HSM_ALERT_KIND_INSTANT;
            AlertConditionData received;
            received.combination = HSM_ALERT_COMBINATION_AND;
            received.property = HSM_ALERT_PROP_NEW_SENSOR_DATA;
            received.operation = HSM_ALERT_OP_RECEIVED_NEW_VALUE;
            received.target_type = HSM_ALERT_TARGET_LAST_VALUE;
            received.target_is_null = true;
            alert.conditions.push_back(received);
            alert.has_template = true;
            alert.template_text = "[$product] $value - $comment";
            registration.alerts.push_back(std::move(alert));

            // ServiceCommandsPrototype: RevealDefaultPath(this, Category=null, SensorName) -> the
            // ".module/Service commands" prototype path (then CalculateSystemPath adds the prefix).
            const std::string prototype_path = RevealDefaultPath(std::string{}, "Service commands", false);
            return CreateSensor(prototype_path.c_str(), HSM_SENSOR_TYPE_STRING, false, std::string{}, out_sensor, registration);
        }

        hsm_result_t AddValueJson(
            const std::string& path,
            hsm_sensor_type_t type,
            const std::string& value_json,
            hsm_sensor_status_t status,
            const char* comment)
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);

                ClearError();

                if (!CanAcceptDataLocked())
                    return HSM_RESULT_OK;
            }

            Enqueue(BuildValueJson(path, type, value_json, status, TrimComment(CopyString(comment))));
            return HSM_RESULT_OK;
        }

        // Bar publish path (roll-on-add): gated on CanAcceptData exactly like an instant value —
        // a bar that closes while the collector is stopped is dropped, not queued.
        void EnqueueIfRunning(std::string json)
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);

                if (!CanAcceptDataLocked())
                    return;
            }

            Enqueue(std::move(json));
        }

        // File payloads are push-driven (the C# file queue wakes on enqueue instead of waiting
        // out PackageCollectPeriod), so the worker is kicked to dispatch promptly.
        void EnqueueFileIfRunning(std::string json)
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);

                if (!CanAcceptDataLocked())
                    return;
            }

            Enqueue(std::move(json));

            {
                std::lock_guard<std::mutex> guard(queue_mutex_);
                dispatch_kick_ = true;
            }

            queue_cv_.notify_all();
        }

        void SetSendFailNext(int32_t count)
        {
            if (count > 0)
                fail_next_.fetch_add(count);
        }

        void SetSendHang(bool hang)
        {
            {
                std::lock_guard<std::mutex> guard(hang_mutex_);
                send_hang_ = hang;
            }

            hang_cv_.notify_all();
        }

        static std::string BuildValueJson(
            const std::string& path,
            hsm_sensor_type_t type,
            const std::string& value_json,
            hsm_sensor_status_t status,
            const std::string& comment)
        {
            std::ostringstream json;
            json << "{"
                 << "\"Type\":" << static_cast<int>(type) << ","
                 << "\"Path\":\"" << EscapeJson(path) << "\","
                 << "\"Value\":" << value_json << ","
                 << "\"Status\":" << static_cast<int>(status) << ","
                 << "\"Comment\":\"" << EscapeJson(comment) << "\","
                 << "\"UnixTimeMs\":" << UnixTimeMilliseconds()
                 << "}";

            return json.str();
        }

        size_t SentCount() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return sent_values_.size();
        }

        hsm_result_t SentJson(size_t index, const char** out_json) const
        {
            if (out_json == nullptr)
                return HSM_RESULT_INVALID_ARGUMENT;

            std::lock_guard<std::mutex> guard(mutex_);

            if (index >= sent_values_.size())
            {
                *out_json = nullptr;
                return SetError(HSM_RESULT_NOT_FOUND, "Sent value was not found.");
            }

            *out_json = sent_values_[index].c_str();
            ClearError();
            return HSM_RESULT_OK;
        }

        size_t RegistrationCount() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return registrations_.size();
        }

        hsm_result_t RegistrationJson(size_t index, const char** out_json) const
        {
            if (out_json == nullptr)
                return HSM_RESULT_INVALID_ARGUMENT;

            std::lock_guard<std::mutex> guard(mutex_);

            if (index >= registrations_.size())
            {
                *out_json = nullptr;
                return SetError(HSM_RESULT_NOT_FOUND, "Registration was not found.");
            }

            *out_json = registrations_[index].c_str();
            ClearError();
            return HSM_RESULT_OK;
        }

        const char* LastError() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return last_error_.c_str();
        }

    private:
        hsm_result_t SetError(hsm_result_t result, std::string message) const
        {
            last_error_ = std::move(message);
            return result;
        }

        void ClearError() const
        {
            last_error_.clear();
        }

        // --- Lifecycle gates (caller holds mutex_). Same phase table as the managed
        // collector (overview.md "Data gating" / "Sensor registration"). ---

        // Data may be queued while Starting/Running/Stopping (so sensors flush at stop);
        // dropped while Stopped or Disposed.
        bool CanAcceptDataLocked() const
        {
            return state_ == CollectorState::Starting || state_ == CollectorState::Running || state_ == CollectorState::Stopping;
        }

        // A new sensor starts (and registers) immediately only while Starting/Running.
        bool CanStartNewSensorsLocked() const
        {
            return state_ == CollectorState::Starting || state_ == CollectorState::Running;
        }

        // Registration is accepted unless shutting down or terminal (Stopping/Disposed):
        // the union of the configuration (Stopped) and operational (Starting/Running) phases.
        bool CanRegisterSensorsLocked() const
        {
            return state_ != CollectorState::Stopping && state_ != CollectorState::Disposed;
        }

        // Fire a lifecycle transition to every listener, exception-isolated. Caller holds
        // op_mutex_ (never mutex_), so transitions are serialized and callbacks observe them in
        // order. The listener set is snapshotted under listeners_mutex_ and the callbacks run
        // OUTSIDE that lock, so a callback may register another listener (no self-deadlock, no
        // mid-iteration mutation). A listener may read collector state but must not re-enter
        // Start/Stop/Dispose (those hold op_mutex_, which this thread already holds).
        void NotifyLifecycle(CollectorState state)
        {
            const auto status = ToPublicStatus(state);

            std::vector<LifecycleListener> listeners;
            {
                std::lock_guard<std::mutex> guard(listeners_mutex_);
                listeners = lifecycle_listeners_;
            }

            for (const auto& listener : listeners)
                InvokeIsolated([&] { listener.callback(status, listener.user_data); });
        }

        void LogMessage(hsm_log_level_t level, const std::string& message)
        {
            hsm_log_callback_t callback = nullptr;
            void* user_data = nullptr;
            {
                std::lock_guard<std::mutex> guard(logger_mutex_);
                callback = log_callback_;
                user_data = log_user_data_;
            }

            if (callback == nullptr)
                return;

            InvokeIsolated([&] { callback(level, message.c_str(), user_data); });
        }

        // Error routing entry point: validation drops, loop errors and shutdown discards all
        // funnel here. Errors pass through the deduplicator (window/capacity from the options)
        // so a storm of identical messages collapses to one log line; the suppressed count is
        // flushed as a "(N suppressed)" suffix on the FIRST recurrence after the window elapses
        // (recurrence-driven, not timer-driven — a storm that simply stops leaves its trailing
        // count unemitted; memory is still bounded by capacity eviction). A zero window logs
        // immediately and returns (no dedup) — the managed zero-window contract / double-log guard.
        void LogError(const std::string& message)
        {
            if (dedup_window_ms_ <= 0)
            {
                LogMessage(HSM_LOG_LEVEL_ERROR, message);
                return;
            }

            const auto now_ms = UnixTimeMilliseconds();
            bool emit = false;
            int64_t suppressed = 0;
            {
                std::lock_guard<std::mutex> guard(logger_mutex_);

                auto it = dedup_.find(message);
                if (it == dedup_.end())
                {
                    EvictDedupIfFullLocked(now_ms);
                    dedup_.emplace(message, DedupEntry{ 0, now_ms });
                    emit = true;
                }
                else if (now_ms - it->second.last_logged_ms >= dedup_window_ms_)
                {
                    suppressed = it->second.suppressed;
                    it->second.suppressed = 0;
                    it->second.last_logged_ms = now_ms;
                    emit = true;
                }
                else
                {
                    ++it->second.suppressed;
                }
            }

            if (emit)
                LogMessage(HSM_LOG_LEVEL_ERROR, suppressed > 0
                                                    ? message + " (" + std::to_string(suppressed) + " suppressed)"
                                                    : message);
        }

        // Caller holds logger_mutex_. Bounds the dedup map to max_deduplicated_messages_ by
        // evicting the oldest entry (smallest last_logged_ms) — same capacity+oldest-expiry
        // policy as the managed MessageDeduplicator.
        void EvictDedupIfFullLocked(int64_t /*now_ms*/)
        {
            if (dedup_.size() < static_cast<size_t>(max_deduplicated_messages_))
                return;

            auto oldest = dedup_.begin();
            for (auto it = dedup_.begin(); it != dedup_.end(); ++it)
                if (it->second.last_logged_ms < oldest->second.last_logged_ms)
                    oldest = it;

            if (oldest != dedup_.end())
                dedup_.erase(oldest);
        }

        std::string BuildSensorPath(const std::string& path) const
        {
            return JoinPathParts({ computer_name_, module_, path });
        }

        // CalculateSystemPath (SensorOptions.cs): a computer sensor anchors at ComputerName/Path; a
        // module sensor at ComputerName/Module/Path; a product sensor at the bare Path (root).
        std::string CalculateSystemPath(const std::string& path, const RegistrationOptions& options) const
        {
            if (options.is_computer_sensor)
                return JoinPathParts({ computer_name_, path });
            if (options.sensor_location == SensorLocation::Product)
                return JoinPathParts({ path });
            return JoinPathParts({ computer_name_, module_, path });
        }

        // RevealDefaultPath (DefaultPrototype.cs): default sensors live under the `.computer`/`.module`
        // folder by category. `.computer` for a computer sensor, `.module` otherwise.
        std::string RevealDefaultPath(const std::string& category, const std::string& path, bool is_computer_sensor) const
        {
            return JoinPathParts({ is_computer_sensor ? ".computer" : ".module", category, path });
        }

        // Caller holds mutex_. New sensors register immediately when the collector can start
        // new sensors (Starting/Running — mirrors C# InitAsync on dynamic creation); sensors
        // created before Start are registered by the Start loop.
        void RegisterSensorLocked(
            const std::shared_ptr<NativeSensor>& sensor,
            hsm_sensor_type_t type,
            const std::string& sensor_path,
            const RegistrationOptions& registration)
        {
            sensor->SetClock(clock_);
            sensor->SetRegistration(sensor_path, type, registration);

            if (CanStartNewSensorsLocked())
            {
                registrations_.push_back(sensor->RegistrationJson());

                // A periodic sensor created while running must post immediately: nudge the
                // scheduler so it re-reads the due-time instead of waiting out its current sleep.
                if (sensor->IsPeriodic())
                    scheduler_task_->Wake();
            }
        }

        // FIFO send queue. Lock discipline: `queue_mutex_` and `mutex_` are never held together —
        // the enqueue path locks mutex_ (state check) then queue_mutex_ sequentially, and the
        // dispatcher locks queue_mutex_ (pop) then mutex_ (record) sequentially.
        void Enqueue(std::string json)
        {
            std::lock_guard<std::mutex> guard(queue_mutex_);

            queue_.push_back(std::move(json));

            // This is the ONLY place a value is dropped: the (large) buffer is full. Monitoring
            // history keeps every value until then; on overflow the oldest is evicted first
            // (position-based FIFO, newest value kept) — same policy as the C# QueueProcessorBase.
            while (queue_.size() > static_cast<size_t>(max_queue_size_))
                queue_.pop_front();
        }

        // Retry re-enqueue (caller holds queue_mutex_). A failed package rotates to the TAIL and is
        // retried — for a monitoring-history buffer every value matters, so a retry is NEVER dropped
        // below capacity. The single drop is the #1088 backstop: when the buffer is already FULL the
        // retry is the oldest data (it was popped from the head, and everything still queued was
        // enqueued behind it while its send was in flight), so it is dropped rather than evicting a
        // queued value — never below capacity, matching normal overflow's newest-wins. No retry cap:
        // a retry rides the tail until delivered or, under sustained overflow, FIFO-evicted.
        void ReEnqueueLocked(std::string json)
        {
            if (queue_.size() >= static_cast<size_t>(max_queue_size_))
                return;

            queue_.push_back(std::move(json));
        }

        void StartWorker()
        {
            {
                std::lock_guard<std::mutex> guard(hang_mutex_);
                send_cancelled_ = false;
            }

#if defined(HSM_COLLECTOR_HTTP)
            // Re-arm the transport's cancellation so sends after a restart are not aborted by a
            // Cancel() left set from the previous Stop.
            if (http_transport_)
                http_transport_->ResetCancel();
#endif

            {
                std::lock_guard<std::mutex> guard(queue_mutex_);
                worker_stop_ = false;
            }

            worker_ = std::thread([this] { WorkerLoop(); });
        }

        void StartScheduler()
        {
            scheduler_task_->Start();
        }

        void StopScheduler()
        {
            scheduler_task_->Stop();
        }

        // Earliest next periodic due time (steady ms) across all periodic sensors; the
        // scheduler sleeps until then instead of polling. Reads each sensor's lock-free hint,
        // so no sensor lock is taken under the collector lock. Far future when nothing periodic.
        int64_t EarliestNextPostMs()
        {
            int64_t earliest = (std::numeric_limits<int64_t>::max)();
            std::lock_guard<std::mutex> guard(mutex_);
            for (const auto& sensor : sensors_)
                if (sensor.second->IsPeriodic())
                    earliest = std::min(earliest, sensor.second->NextPostHint());

            return earliest;
        }

        // Each due periodic sensor builds its post under its own lock and publishes through the
        // data gate. Sensor locks are taken strictly outside the collector lock (snapshot
        // first), same one-way order as everywhere else. A throwing user function callback is
        // isolated so it cannot kill the scheduler loop or starve the other sensors (onError).
        void TickPeriodicSensors()
        {
            std::vector<std::shared_ptr<NativeSensor>> periodic;
            {
                std::lock_guard<std::mutex> guard(mutex_);

                for (const auto& sensor : sensors_)
                    if (sensor.second->IsPeriodic())
                        periodic.push_back(sensor.second);
            }

            for (const auto& sensor : periodic)
            {
                InvokeIsolated([&] {
                    std::string json;
                    if (sensor->TryBuildPeriodicJson(json))
                        EnqueueIfRunning(std::move(json));
                });
            }
        }

        void StopWorker()
        {
            // Cancel in-flight sends FIRST so a worker blocked inside a hung TrySendBatch wakes
            // up (the hung send fails, its batch is re-enqueued) and the join below stays bounded.
            {
                std::lock_guard<std::mutex> guard(hang_mutex_);
                send_cancelled_ = true;
            }

            hang_cv_.notify_all();

#if defined(HSM_COLLECTOR_HTTP)
            // Abort an in-flight POST so a worker blocked in libcurl wakes up (the send fails, the
            // batch re-enqueues, the stop drain drops it) and the join below stays bounded. The
            // xfer-abort reads a lock-free atomic, so this is safe without the hang/queue locks.
            if (http_transport_)
                http_transport_->Cancel();
#endif

            {
                std::lock_guard<std::mutex> guard(queue_mutex_);
                worker_stop_ = true;
            }

            queue_cv_.notify_all();

            if (worker_.joinable())
                worker_.join();
        }

        void WorkerLoop()
        {
            std::unique_lock<std::mutex> lock(queue_mutex_);

            while (!worker_stop_)
            {
                queue_cv_.wait_for(
                    lock,
                    std::chrono::milliseconds(collect_period_ms_),
                    [this] { return worker_stop_ || dispatch_kick_; });

                if (worker_stop_)
                    break;

                dispatch_kick_ = false;
                DispatchQueuedLocked(lock, /*clear_remainder_on_failure=*/false);
            }
        }

        void DrainQueueOnStop()
        {
            size_t dropped = 0;
            {
                std::unique_lock<std::mutex> lock(queue_mutex_);
                dropped = DispatchQueuedLocked(lock, /*clear_remainder_on_failure=*/true);
            }

            // Error routing — shutdown discard: a bounded stop that drops pending values on a
            // dead/failing transport reports it (logged outside the queue lock so a slow log
            // sink cannot stall shutdown). Deduplicated like any other error.
            if (dropped > 0)
                LogError("Collector stop dropped " + std::to_string(dropped) + " pending value(s): transport unavailable.");
        }

        // Pops and sends batches of up to max_values_in_package_ until the queue is empty or a
        // send fails. The lock is dropped around the send so enqueues never wait on dispatch.
        // On failure the batch is re-enqueued at the TAIL (ReEnqueueLocked: kept unless the buffer
        // is full, in which case the retry — the oldest data — is dropped by the #1088 backstop) —
        // or, during the stop flush, everything left is dropped so shutdown cannot hang on a
        // failing transport. Returns the number of values dropped (only non-zero on the
        // clear-on-failure stop path).
        size_t DispatchQueuedLocked(std::unique_lock<std::mutex>& lock, bool clear_remainder_on_failure)
        {
            while (!queue_.empty())
            {
                std::vector<std::string> batch;
                const auto batch_size = std::min(queue_.size(), static_cast<size_t>(max_values_in_package_));
                batch.reserve(batch_size);

                for (size_t index = 0; index < batch_size; ++index)
                {
                    batch.push_back(std::move(queue_.front()));
                    queue_.pop_front();
                }

                lock.unlock();
                const auto sent = TrySendBatch(batch);
                lock.lock();

                if (!sent)
                {
                    if (clear_remainder_on_failure)
                    {
                        const size_t dropped = batch.size() + queue_.size();
                        queue_.clear();
                        return dropped;
                    }

                    for (auto& json : batch)
                        ReEnqueueLocked(std::move(json));

                    return 0;
                }
            }

            return 0;
        }

        bool TrySendBatch(std::vector<std::string>& batch)
        {
            // Injected transport hang: block until the hang is lifted or the stop path cancels
            // in-flight sends. A cancelled hung send is a failed send — the worker re-enqueues
            // the batch and the stop drain drops it, keeping shutdown bounded.
            {
                std::unique_lock<std::mutex> lock(hang_mutex_);

                if (send_hang_)
                {
                    hang_cv_.wait(lock, [this] { return !send_hang_ || send_cancelled_; });

                    if (send_hang_)
                        return false;
                }
            }

            // The injected failure fires BEFORE anything is recorded — mirrors the C# harness's
            // RecordingSender, whose failure must throw before any value is captured.
            if (TryConsumeFailToken())
                return false;

            return sender_(batch);
        }

        // Default send seam: record the batch for the behavior corpus + the sent_count/get_sent_json
        // accessors. Swapped for a libcurl POST by TestInstallHttpSender (HTTP build) before Start.
        bool RecordBatch(std::vector<std::string>& batch)
        {
            std::lock_guard<std::mutex> guard(mutex_);

            for (auto& json : batch)
                sent_values_.push_back(std::move(json));

            return true;
        }

#if defined(HSM_COLLECTOR_HTTP)
        // One POST attempt per batch. A non-2xx or transport failure returns false, so the
        // dispatcher re-enqueues the batch at the tail — the same durable-retry contract the C#
        // queue uses (it re-enqueues on PackageSendingInfo.Error != null, including 5xx and 4xx).
        // The Polly-equivalent in-send backoff (RetryPolicy, unit-tested separately) is a
        // non-blocking refinement layered on later; doing it inline would block the single worker
        // thread for up to the RetryPolicy max-delay per batch.
        bool HttpSendBatch(std::vector<std::string>& batch)
        {
            // The data queue batches values to /list (DataHandlers RouteForSensorValue is_batch=true)
            // as a JSON array; each element is the already-serialized wire value.
            std::string body = "[";
            for (size_t index = 0; index < batch.size(); ++index)
            {
                if (index != 0)
                    body += ',';
                body += batch[index];
            }
            body += "]";

            const std::vector<hsm::http::HttpHeader> headers = {
                { "Key", access_key_ },
                { "ClientName", client_name_ },
                { "Content-Type", "application/json" },
            };

            const auto response = http_transport_->Post(endpoints_.List(), body, headers);
            return response.IsSuccess();
        }
#endif

        bool TryConsumeFailToken()
        {
            auto current = fail_next_.load();

            while (current > 0)
            {
                if (fail_next_.compare_exchange_weak(current, current - 1))
                    return true;
            }

            return false;
        }

        mutable std::mutex mutex_;
        CollectorState state_ = CollectorState::Stopped;
        std::unordered_map<std::string, std::shared_ptr<NativeSensor>> sensors_;
        std::vector<std::shared_ptr<AlertData>> alert_data_; // alert handles, owned for the collector's lifetime
        std::vector<std::string> sent_values_;
        std::vector<std::string> registrations_;
        mutable std::string last_error_;

        std::mutex queue_mutex_;
        std::condition_variable queue_cv_;
        std::deque<std::string> queue_;
        bool worker_stop_ = false;
        bool dispatch_kick_ = false;
        std::thread worker_;
        std::atomic<int32_t> fail_next_{ 0 };

        std::mutex hang_mutex_;
        std::condition_variable hang_cv_;
        bool send_hang_ = false;
        bool send_cancelled_ = false;

        // Send seam: TrySendBatch delegates here. Default records into sent_values_; the HTTP build
        // swaps in a libcurl POST via TestInstallHttpSender (installed before Start, read only by
        // the worker thread after Start — no lock needed for the swap).
        std::function<bool(std::vector<std::string>&)> sender_;
#if defined(HSM_COLLECTOR_HTTP)
        std::unique_ptr<hsm::http::HttpTransport> http_transport_;
        hsm::http::Endpoints endpoints_;
#endif

        // Periodic scheduler (issue #1095 §13): a single ScheduledTask worker that sleeps
        // until the earliest sensor due-time, read through the swappable clock seam. The
        // clock is shared with each periodic sensor (via SetClock at registration) so a test
        // clock drives both the wait and the sensors' due-checks.
        std::shared_ptr<Clock> clock_ = std::make_shared<RealClock>();
        std::unique_ptr<ScheduledTask> scheduler_task_;

        // Lifecycle operations (Start/Stop/Dispose) serialize on this coarse lock so a
        // dispose racing a stop joins the in-flight transition rather than duplicating
        // it. Listeners are invoked under it (after state_ flips) for consistent order.
        std::mutex op_mutex_;

        // Lifecycle listeners have their own lock: NotifyLifecycle snapshots under it and
        // invokes callbacks outside it, so a callback may add a listener without deadlocking.
        std::mutex listeners_mutex_;
        std::vector<LifecycleListener> lifecycle_listeners_;

        // Pluggable log sink + error deduplicator (overview.md "error-handling").
        std::mutex logger_mutex_;
        hsm_log_callback_t log_callback_ = nullptr;
        void* log_user_data_ = nullptr;
        std::unordered_map<std::string, DedupEntry> dedup_;

        // [[maybe_unused]] marks options stored to mirror CollectorOptions but consumed only by
        // the HTTP transport (#1096), which has not landed yet — this keeps the clang
        // -Wunused-private-field lane (under -Werror) green without reordering the members
        // (which would trip -Wreorder against the constructor initializer list).
        std::string access_key_;
        std::string server_address_;
        [[maybe_unused]] int32_t port_;
        std::string client_name_;
        std::string module_;
        std::string computer_name_;
        int32_t max_queue_size_;
        int32_t max_values_in_package_;
        int32_t collect_period_ms_;
        [[maybe_unused]] int32_t request_timeout_ms_;
        int32_t max_sensors_;
        [[maybe_unused]] bool allow_untrusted_certificate_;
        [[maybe_unused]] bool allow_plaintext_transport_;
        int64_t dedup_window_ms_;
        int32_t max_deduplicated_messages_;
    };

    hsm_result_t NativeSensor::AddInt(int32_t value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_INT)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(std::to_string(value), status, comment);
    }

    hsm_result_t NativeSensor::AddBool(bool value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_BOOLEAN)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(value ? "true" : "false", status, comment);
    }

    hsm_result_t NativeSensor::AddDouble(double value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_DOUBLE)
            return HSM_RESULT_INVALID_ARGUMENT;

        if (!std::isfinite(value))
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(DoubleJson(value), status, comment);
    }

    hsm_result_t NativeSensor::AddString(const char* value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_STRING)
            return HSM_RESULT_INVALID_ARGUMENT;

        if (value == nullptr)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson("\"" + EscapeJson(CopyString(value)) + "\"", status, comment);
    }

    hsm_result_t NativeSensor::AddEnum(int32_t value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_ENUM)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson(std::to_string(value), status, comment);
    }

    hsm_result_t NativeSensor::AddTimeSpan(int64_t ticks, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_TIMESPAN)
            return HSM_RESULT_INVALID_ARGUMENT;

        // Value is the "c"-formatted TimeSpan as a JSON string (ASCII-only, so EscapeJson is a no-op).
        return AddValueJson("\"" + EscapeJson(TimeSpanCFormat(ticks)) + "\"", status, comment);
    }

    hsm_result_t NativeSensor::AddVersion(int32_t major, int32_t minor, int32_t build, int32_t revision, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_VERSION)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AddValueJson("\"" + EscapeJson(VersionString(major, minor, build, revision)) + "\"", status, comment);
    }

    hsm_result_t NativeSensor::AddValueJson(std::string value_json, hsm_sensor_status_t status, const char* comment)
    {
        if (!IsValidStatus(status))
            return HSM_RESULT_INVALID_ARGUMENT;

        const auto collector = collector_.lock();
        if (!collector)
            return HSM_RESULT_INVALID_STATE;

        if (is_last_value_)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            last_value_json_ = std::move(value_json);
            last_status_ = status;
            last_comment_ = TrimComment(CopyString(comment));
            return HSM_RESULT_OK;
        }

        return collector->AddValueJson(path_, type_, value_json, status, comment);
    }

    hsm_result_t NativeSensor::AddBarInt(int32_t value)
    {
        if (type_ != HSM_SENSOR_TYPE_INT_BAR)
            return HSM_RESULT_INVALID_ARGUMENT;

        return AccumulateBar([value](MonitoringBar& bar) { bar.AddValue(static_cast<double>(value)); });
    }

    hsm_result_t NativeSensor::AddBarDouble(double value)
    {
        if (type_ != HSM_SENSOR_TYPE_DOUBLE_BAR)
            return HSM_RESULT_INVALID_ARGUMENT;

        // Silent skip, not an error: matches the C# bar AddValue contract (log-and-continue).
        if (!std::isfinite(value))
            return HSM_RESULT_OK;

        return AccumulateBar([value](MonitoringBar& bar) { bar.AddValue(value); });
    }

    hsm_result_t NativeSensor::AddBarIntPartial(int32_t min, int32_t max, int32_t mean, int32_t first, int32_t last, int32_t count)
    {
        if (type_ != HSM_SENSOR_TYPE_INT_BAR)
            return HSM_RESULT_INVALID_ARGUMENT;

        if (!IsValidIntPartial(min, max, mean, first, last, count))
            return HSM_RESULT_OK;

        return AccumulateBar([=](MonitoringBar& bar) {
            bar.AddPartial(
                static_cast<double>(min),
                static_cast<double>(max),
                static_cast<double>(mean),
                static_cast<double>(first),
                static_cast<double>(last),
                count);
        });
    }

    hsm_result_t NativeSensor::AddBarDoublePartial(double min, double max, double mean, double first, double last, int32_t count)
    {
        if (type_ != HSM_SENSOR_TYPE_DOUBLE_BAR)
            return HSM_RESULT_INVALID_ARGUMENT;

        if (!IsValidDoublePartial(min, max, mean, first, last, count))
            return HSM_RESULT_OK;

        return AccumulateBar([=](MonitoringBar& bar) { bar.AddPartial(min, max, mean, first, last, count); });
    }

    template <typename Accumulate>
    hsm_result_t NativeSensor::AccumulateBar(Accumulate&& accumulate)
    {
        const auto collector = collector_.lock();
        if (!collector)
            return HSM_RESULT_INVALID_STATE;

        // Roll-on-add: a value arriving past the close publishes the closed bar and opens a
        // fresh aligned one. The closed-bar JSON is built under the sensor lock but published
        // after releasing it — the collector takes sensor locks while holding its own, so the
        // reverse nesting here would deadlock.
        std::string closed_json;
        {
            std::lock_guard<std::mutex> guard(mutex_);

            const auto now_ms = UnixTimeMilliseconds();
            if (bar_.close_ms < now_ms)
            {
                if (bar_.count > 0)
                    closed_json = MonitoringBarJson(bar_, path_);

                bar_.Init(now_ms);
            }

            accumulate(bar_);
        }

        if (!closed_json.empty())
            collector->EnqueueIfRunning(std::move(closed_json));

        return HSM_RESULT_OK;
    }

    hsm_result_t NativeSensor::AddRate(double value, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_RATE)
            return HSM_RESULT_INVALID_ARGUMENT;

        // Silent skip on invalid value/status — neither the sum nor the sticky state change
        // (mirrors C# MonitoringRateSensor.AddValue: log-and-return).
        if (!std::isfinite(value) || !IsValidStatus(status))
            return HSM_RESULT_OK;

        std::lock_guard<std::mutex> guard(mutex_);

        rate_sum_ += value;
        rate_status_ = status;
        rate_comment_ = TrimComment(CopyString(comment));
        return HSM_RESULT_OK;
    }

    hsm_result_t NativeSensor::AddFunctionInt(int32_t value)
    {
        if (int_values_function_ == nullptr)
            return HSM_RESULT_INVALID_ARGUMENT;

        std::lock_guard<std::mutex> guard(mutex_);

        function_values_.push_back(value);

        while (function_values_.size() > static_cast<size_t>(function_max_cache_))
            function_values_.pop_front();

        return HSM_RESULT_OK;
    }

    hsm_result_t NativeSensor::AddFile(const char* utf8_content, hsm_sensor_status_t status, const char* comment)
    {
        if (type_ != HSM_SENSOR_TYPE_FILE)
            return HSM_RESULT_INVALID_ARGUMENT;

        // Mirrors C# FileSensorInstant.AddValue: null content and invalid status are silent no-ops.
        if (utf8_content == nullptr || !IsValidStatus(status))
            return HSM_RESULT_OK;

        const auto collector = collector_.lock();
        if (!collector)
            return HSM_RESULT_INVALID_STATE;

        const auto normalized_comment = TrimComment(CopyString(comment));

        // Canonical cross-language file payload (string content asserted as UTF-8 text).
        std::ostringstream json;
        json << "{"
             << "\"Type\":" << static_cast<int>(HSM_SENSOR_TYPE_FILE) << ","
             << "\"Path\":\"" << EscapeJson(path_) << "\","
             << "\"Value\":\"" << EscapeJson(CopyString(utf8_content)) << "\","
             << "\"Name\":\"" << EscapeJson(file_name_) << "\","
             << "\"Extension\":\"" << EscapeJson(file_extension_) << "\","
             << "\"Status\":" << static_cast<int>(status) << ","
             << "\"Comment\":\"" << EscapeJson(normalized_comment) << "\","
             << "\"UnixTimeMs\":" << UnixTimeMilliseconds()
             << "}";

        // Instant gating: dropped unless the collector is running (values before Start are
        // lost). File payloads dispatch promptly — not gated by the package collect period.
        collector->EnqueueFileIfRunning(json.str());
        return HSM_RESULT_OK;
    }

    void NativeSensor::ResetPeriodicBaseline()
    {
        if (!is_periodic_)
            return;

        std::lock_guard<std::mutex> guard(mutex_);

        // Immediate first post on (re)start; the rate baseline resets so the first sample of a
        // new run does not divide by the stopped gap (mirrors C# MonitoringRateSensor.InitAsync).
        next_post_ms_ = SteadyNowMs();
        next_post_hint_.store(next_post_ms_);
        rate_has_prev_ = false;
    }

    bool NativeSensor::TryBuildPeriodicJson(std::string& out_json)
    {
        if (!is_periodic_)
            return false;

        // The sensor lock covers only the due-check and the mutable-state snapshot. User
        // callbacks run OUTSIDE it: a callback that re-enters the same sensor (AddRate /
        // AddFunctionInt) must not deadlock on the non-recursive mutex, and arbitrary user
        // code must not block concurrent AddValue calls. The callback pointers and user_data
        // are immutable after construction, so reading them unlocked is safe.
        std::vector<int32_t> values_snapshot;

        {
            std::lock_guard<std::mutex> guard(mutex_);

            const auto now_ms = SteadyNowMs();
            if (now_ms < next_post_ms_)
                return false;

            // Catch up by whole periods: if the worker (or a virtual-clock jump) skipped past
            // several periods, advance to the next future boundary and emit once — a fixed
            // cadence from the start anchor, no backlog of posts (managed fixed-rate schedule).
            // The C ABI rejects a non-positive period at creation; the guard is defensive so a
            // zero/negative period can never spin this loop forever under the sensor lock.
            if (post_period_ms_ > 0)
            {
                do
                    next_post_ms_ += post_period_ms_;
                while (next_post_ms_ <= now_ms);
            }
            else
            {
                next_post_ms_ = now_ms + 1;
            }
            next_post_hint_.store(next_post_ms_);

            if (type_ == HSM_SENSOR_TYPE_RATE)
            {
                // Divide by the measured elapsed time since the previous post; the first sample
                // falls back to the configured period (C# #1102-E2 contract). Elapsed is read
                // through the clock seam (ms granularity).
                double seconds = post_period_ms_ / 1000.0;
                if (rate_has_prev_)
                {
                    const double elapsed = (now_ms - rate_prev_ms_) / 1000.0;
                    if (elapsed > 0)
                        seconds = elapsed;
                }

                rate_prev_ms_ = now_ms;
                rate_has_prev_ = true;

                const double rate = seconds > 0 ? rate_sum_ / seconds : 0.0;
                rate_sum_ = 0.0;

                out_json = NativeCollector::BuildValueJson(path_, HSM_SENSOR_TYPE_RATE, DoubleJson(rate), rate_status_, rate_comment_);
                return true;
            }

            if (int_values_function_ != nullptr)
            {
                // Snapshot of the sliding window — the buffer itself is NOT drained.
                values_snapshot.assign(function_values_.begin(), function_values_.end());
            }
        }

        if (int_function_ != nullptr)
        {
            const auto value = int_function_(function_user_data_);
            out_json = NativeCollector::BuildValueJson(path_, HSM_SENSOR_TYPE_INT, std::to_string(value), HSM_SENSOR_STATUS_OK, std::string{});
            return true;
        }

        if (int_values_function_ != nullptr)
        {
            const auto value = int_values_function_(
                values_snapshot.empty() ? nullptr : values_snapshot.data(),
                static_cast<int32_t>(values_snapshot.size()),
                function_user_data_);

            out_json = NativeCollector::BuildValueJson(path_, HSM_SENSOR_TYPE_INT, std::to_string(value), HSM_SENSOR_STATUS_OK, std::string{});
            return true;
        }

        return false;
    }

    bool NativeSensor::IsPeriodic() const
    {
        return is_periodic_;
    }

    // Distinguishes the periodic sub-kind for duplicate-path detection: rate (both null),
    // no-params function, and values-function all share Type-level identity otherwise (the two
    // function flavors are both Type == INT). Only the callback SHAPE participates — a duplicate
    // create with the same shape but a different callback/user_data still dedupes to the
    // existing sensor, matching the handle-dedup semantics of the other sensor families.
    bool NativeSensor::MatchesPeriodicShape(hsm_int_function_t int_function, hsm_int_values_function_t int_values_function) const
    {
        return (int_function_ != nullptr) == (int_function != nullptr) &&
               (int_values_function_ != nullptr) == (int_values_function != nullptr);
    }

    bool NativeSensor::TryFlushBarJson(std::string& out_json)
    {
        if (!is_bar_)
            return false;

        std::lock_guard<std::mutex> guard(mutex_);

        if (bar_.count <= 0)
            return false;

        out_json = MonitoringBarJson(bar_, path_);

        // Roll after a successful flush so a stop -> restart -> stop cycle never resends the bar.
        bar_.Init(UnixTimeMilliseconds());
        return true;
    }

    bool NativeSensor::TryGetLastValueSnapshot(SensorSnapshot& snapshot) const
    {
        if (!is_last_value_)
            return false;

        std::lock_guard<std::mutex> guard(mutex_);
        snapshot.path = path_;
        snapshot.type = type_;
        snapshot.value_json = last_value_json_;
        snapshot.status = last_status_;
        snapshot.comment = last_comment_;
        return true;
    }

    hsm_sensor_type_t NativeSensor::Type() const
    {
        return type_;
    }

    bool NativeSensor::IsLastValue() const
    {
        return is_last_value_;
    }
} // namespace

struct hsm_collector_t
{
    std::shared_ptr<NativeCollector> impl;
};

struct hsm_sensor_t
{
    std::shared_ptr<NativeSensor> impl;
};

static hsm_result_t CreateSensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    bool is_last_value,
    const std::string& default_value_json,
    hsm_sensor_t** out_sensor,
    const RegistrationOptions& registration = InstantRegistrationDefaults());

int32_t hsm_collector_version(void)
{
    return HSM_COLLECTOR_VERSION;
}

hsm_result_t hsm_collector_create(const hsm_collector_options_t* options, hsm_collector_t** out_collector)
{
    if (out_collector != nullptr)
        *out_collector = nullptr;

    if (options == nullptr || out_collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    if (options->access_key == nullptr || IsBlank(CopyString(options->access_key)))
        return HSM_RESULT_INVALID_ARGUMENT;

    if (options->server_address == nullptr || IsBlank(CopyString(options->server_address)))
        return HSM_RESULT_INVALID_ARGUMENT;

    if (options->port <= 0 || options->port > 65535)
        return HSM_RESULT_INVALID_ARGUMENT;

    // 0 means "managed default" for every numeric field (the dedup window's 0 means
    // log-immediately); a negative value is always invalid.
    if (options->max_queue_size < 0 || options->max_values_in_package < 0 || options->package_collect_period_ms < 0 || options->request_timeout_ms < 0 || options->max_sensors < 0 || options->exception_deduplicator_window_ms < 0 || options->max_deduplicated_messages < 0)
        return HSM_RESULT_INVALID_ARGUMENT;

    try
    {
        *out_collector = new hsm_collector_t{ std::make_shared<NativeCollector>(*options) };
        return HSM_RESULT_OK;
    }
    catch (const std::exception&)
    {
        if (out_collector != nullptr)
            *out_collector = nullptr;

        return HSM_RESULT_INTERNAL_ERROR;
    }
}

void hsm_collector_destroy(hsm_collector_t* collector)
{
    delete collector;
}

hsm_result_t hsm_collector_start(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->Start();
}

hsm_result_t hsm_collector_stop(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->Stop();
}

hsm_collector_status_t hsm_collector_status(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_COLLECTOR_STATUS_DISPOSED;

    return collector->impl->Status();
}

void hsm_collector_dispose(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return;

    collector->impl->Dispose();
}

hsm_result_t hsm_collector_test_connection(hsm_collector_t* collector)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->TestConnection();
}

hsm_result_t hsm_collector_add_lifecycle_listener(
    hsm_collector_t* collector,
    hsm_lifecycle_callback_t callback,
    void* user_data)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->AddLifecycleListener(callback, user_data);
}

hsm_result_t hsm_collector_set_logger(hsm_collector_t* collector, hsm_log_callback_t callback, void* user_data)
{
    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    collector->impl->SetLogger(callback, user_data);
    return HSM_RESULT_OK;
}

// Test-only seam — deliberately NOT declared in the public header. The native unit tests
// forward-declare these to drive the scheduler's clock without real-time sleeps (the
// injectable clock seam, issue #1095 §13). Install before Start; advance while running.
extern "C" void hsm_collector_test_install_manual_clock(hsm_collector_t* collector, int64_t base_ms)
{
    if (collector != nullptr)
        collector->impl->TestInstallManualClock(base_ms);
}

extern "C" void hsm_collector_test_advance_clock_ms(hsm_collector_t* collector, int64_t delta_ms)
{
    if (collector != nullptr)
        collector->impl->TestAdvanceClock(delta_ms);
}

extern "C" void hsm_collector_test_log_error(hsm_collector_t* collector, const char* message)
{
    if (collector != nullptr && message != nullptr)
        collector->impl->TestLogError(message);
}

#if defined(HSM_COLLECTOR_HTTP)
// Test-only seam (#1097): swap the recording sender for the live libcurl transport so the native
// unit tests exercise the real queue -> worker -> POST path against the in-proc capture server.
// Install before Start (same contract as the clock seam). HTTP-build only.
extern "C" void hsm_collector_test_install_http_sender(hsm_collector_t* collector)
{
    if (collector != nullptr && collector->impl != nullptr)
        collector->impl->TestInstallHttpSender();
}
#endif

// Test-only seam (#1096): exercise the real-wire serializers directly from the native unit
// tests without a live collector. Return into a thread-local buffer (valid until the next call
// on the same thread). Not in the public header.
extern "C" const char* hsm_collector_test_iso_from_unix_ms(int64_t unix_ms)
{
    static thread_local std::string buffer;
    buffer = IsoUtcFromUnixMs(unix_ms);
    return buffer.c_str();
}

extern "C" const char* hsm_collector_test_timespan_c_format(int64_t ticks)
{
    static thread_local std::string buffer;
    buffer = TimeSpanCFormat(ticks);
    return buffer.c_str();
}

extern "C" const char* hsm_collector_test_wire_value_json(
    int32_t type,
    const char* value_json,
    const char* comment,
    int comment_is_null,
    int32_t status,
    int64_t time_ms,
    const char* path)
{
    static thread_local std::string buffer;
    buffer = BuildWireValueJson(
        static_cast<hsm_sensor_type_t>(type),
        value_json != nullptr ? value_json : "",
        comment != nullptr ? comment : "",
        comment_is_null != 0,
        static_cast<hsm_sensor_status_t>(status),
        time_ms,
        path != nullptr ? path : "");
    return buffer.c_str();
}

extern "C" const char* hsm_collector_test_wire_bar_json(
    int is_int,
    double min,
    double max,
    double total_sum,
    double first,
    double last,
    int32_t count,
    int precision,
    int64_t open_ms,
    int64_t close_ms,
    int64_t time_ms,
    const char* path)
{
    static thread_local std::string buffer;
    MonitoringBar bar;
    bar.is_int = is_int != 0;
    bar.precision = precision;
    bar.open_ms = open_ms;
    bar.close_ms = close_ms;
    bar.total_sum = total_sum;
    bar.min = min;
    bar.max = max;
    bar.first = first;
    bar.last = last;
    bar.count = count;
    buffer = BuildWireBarJson(bar, time_ms, path != nullptr ? path : "");
    return buffer.c_str();
}

extern "C" const char* hsm_collector_test_wire_file_json(
    const char* extension,
    const char* name,
    const char* content,
    const char* comment,
    int comment_is_null,
    int32_t status,
    int64_t time_ms,
    const char* path)
{
    static thread_local std::string buffer;
    buffer = BuildWireFileJson(
        extension != nullptr ? extension : "",
        name != nullptr ? name : "",
        content != nullptr ? content : "",
        comment != nullptr ? comment : "",
        comment_is_null != 0,
        static_cast<hsm_sensor_status_t>(status),
        time_ms,
        path != nullptr ? path : "");
    return buffer.c_str();
}

extern "C" const char* hsm_collector_test_wire_registration_json(
    int32_t type,
    int64_t ttl_ms,
    int32_t unit,
    int has_description,
    const char* description,
    int has_enum,
    int32_t enum_key,
    const char* enum_value,
    const char* enum_description,
    int32_t enum_color,
    const char* path)
{
    static thread_local std::string buffer;
    RegistrationOptions options;
    options.ttl_ms = ttl_ms;
    options.unit = unit;
    options.has_description = has_description != 0;
    options.description = description != nullptr ? description : "";
    if (has_enum != 0)
    {
        options.has_enum_options = true;
        options.enum_options.push_back(EnumOptionData{
            enum_key,
            enum_value != nullptr ? enum_value : "",
            enum_color,
            enum_description != nullptr ? enum_description : "" });
    }
    buffer = BuildWireRegistrationJson(path != nullptr ? path : "", static_cast<hsm_sensor_type_t>(type), options);
    return buffer.c_str();
}

hsm_result_t hsm_collector_create_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_INT, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_BOOLEAN, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_DOUBLE, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_STRING, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_enum_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_ENUM, false, std::string{}, out_sensor, EnumRegistrationDefaults());
}

hsm_result_t hsm_collector_create_timespan_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_TIMESPAN, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_version_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_VERSION, false, std::string{}, out_sensor);
}

hsm_result_t hsm_collector_create_last_value_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int32_t default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_INT, true, std::to_string(default_value), out_sensor);
}

hsm_result_t hsm_collector_create_last_value_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    bool default_value,
    hsm_sensor_t** out_sensor)
{
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_BOOLEAN, true, default_value ? "true" : "false", out_sensor);
}

hsm_result_t hsm_collector_create_last_value_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    double default_value,
    hsm_sensor_t** out_sensor)
{
    if (!std::isfinite(default_value))
    {
        if (out_sensor != nullptr)
            *out_sensor = nullptr;

        return HSM_RESULT_INVALID_ARGUMENT;
    }

    return CreateSensor(collector, path, HSM_SENSOR_TYPE_DOUBLE, true, DoubleJson(default_value), out_sensor);
}

hsm_result_t hsm_collector_create_last_value_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    const char* default_value,
    hsm_sensor_t** out_sensor)
{
    if (default_value == nullptr)
    {
        if (out_sensor != nullptr)
            *out_sensor = nullptr;

        return HSM_RESULT_INVALID_ARGUMENT;
    }

    return CreateSensor(
        collector,
        path,
        HSM_SENSOR_TYPE_STRING,
        true,
        "\"" + EscapeJson(CopyString(default_value)) + "\"",
        out_sensor);
}

static hsm_result_t CreateSensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    bool is_last_value,
    const std::string& default_value_json,
    hsm_sensor_t** out_sensor,
    const RegistrationOptions& registration)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateSensor(path, type, is_last_value, default_value_json, sensor, registration);
    if (result != HSM_RESULT_OK)
        return result;

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

hsm_result_t hsm_collector_create_int_sensor_with_options(
    hsm_collector_t* collector,
    const char* path,
    int64_t ttl_ms,
    int32_t unit,
    const char* description,
    hsm_sensor_t** out_sensor)
{
    auto registration = InstantRegistrationDefaults();
    registration.ttl_ms = ttl_ms;
    registration.unit = unit;
    registration.description = CopyString(description);

    return CreateSensor(collector, path, HSM_SENSOR_TYPE_INT, false, std::string{}, out_sensor, registration);
}

hsm_sensor_options_t hsm_sensor_options_default(void)
{
    hsm_sensor_options_t options{};
    options.ttl_ms = 0;
    options.unit = -1;
    options.description = nullptr;
    options.keep_history_ms = 0;
    options.self_destroy_ms = 0;
    options.display_unit = -1;
    options.statistics = -1;
    options.is_singleton = -1;
    options.aggregate_data = -1;
    options.enable_grafana = -1;
    options.is_computer_sensor = false;
    options.sensor_location = 0;
    return options;
}

hsm_result_t hsm_collector_create_sensor_with_options(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    const hsm_sensor_options_t* options,
    hsm_sensor_t** out_sensor)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (options == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto registration = InstantRegistrationDefaults(); // has_description=true (instant default "")
    registration.ttl_ms = options->ttl_ms;
    registration.unit = options->unit;
    registration.description = CopyString(options->description);

    if (options->keep_history_ms > 0)
    {
        registration.has_keep_history = true;
        registration.keep_history_ms = options->keep_history_ms;
    }
    if (options->self_destroy_ms > 0)
    {
        registration.has_self_destroy = true;
        registration.self_destroy_ms = options->self_destroy_ms;
    }
    if (options->display_unit >= 0)
    {
        registration.has_display_unit = true;
        registration.display_unit = options->display_unit;
    }
    if (options->statistics >= 0)
    {
        registration.has_statistics = true;
        registration.statistics = options->statistics;
    }
    registration.is_singleton = static_cast<TriBool>(options->is_singleton);
    registration.aggregate_data = static_cast<TriBool>(options->aggregate_data);
    registration.enable_grafana = static_cast<TriBool>(options->enable_grafana);
    registration.is_computer_sensor = options->is_computer_sensor;
    registration.sensor_location = options->sensor_location == 1 ? SensorLocation::Product : SensorLocation::Module;

    return CreateSensor(collector, path, type, false, std::string{}, out_sensor, registration);
}

hsm_result_t hsm_collector_create_enum_sensor_with_options(
    hsm_collector_t* collector,
    const char* path,
    const char* description,
    const hsm_enum_option_t* enum_options,
    size_t enum_option_count,
    hsm_sensor_t** out_sensor)
{
    if (enum_options == nullptr && enum_option_count > 0)
    {
        if (out_sensor != nullptr)
            *out_sensor = nullptr;
        return HSM_RESULT_INVALID_ARGUMENT;
    }

    auto registration = EnumRegistrationDefaults();
    registration.description = CopyString(description);
    registration.has_enum_options = true;
    registration.enum_options.reserve(enum_option_count);
    for (size_t i = 0; i < enum_option_count; ++i)
        registration.enum_options.push_back(EnumOptionData{
            enum_options[i].key,
            CopyString(enum_options[i].value),
            enum_options[i].color,
            CopyString(enum_options[i].description) });

    return CreateSensor(collector, path, HSM_SENSOR_TYPE_ENUM, false, std::string{}, out_sensor, registration);
}

size_t hsm_collector_registration_count(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return 0;

    return collector->impl->RegistrationCount();
}

hsm_result_t hsm_collector_get_registration_json(const hsm_collector_t* collector, size_t index, const char** out_json)
{
    if (out_json != nullptr)
        *out_json = nullptr;

    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->RegistrationJson(index, out_json);
}

static hsm_result_t CreateBarSensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    int64_t bar_period_ms,
    int64_t post_period_ms,
    int32_t precision,
    hsm_sensor_t** out_sensor)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    if (bar_period_ms <= 0 || post_period_ms < 0)
        return HSM_RESULT_INVALID_ARGUMENT;

    if (precision < 0 || precision > 15)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateBarSensor(path, type, bar_period_ms, precision, sensor);
    if (result != HSM_RESULT_OK)
        return result;

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

hsm_result_t hsm_collector_create_int_bar_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t bar_period_ms,
    int64_t post_period_ms,
    hsm_sensor_t** out_sensor)
{
    return CreateBarSensor(collector, path, HSM_SENSOR_TYPE_INT_BAR, bar_period_ms, post_period_ms, 0, out_sensor);
}

hsm_result_t hsm_collector_create_double_bar_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t bar_period_ms,
    int64_t post_period_ms,
    int32_t precision,
    hsm_sensor_t** out_sensor)
{
    return CreateBarSensor(collector, path, HSM_SENSOR_TYPE_DOUBLE_BAR, bar_period_ms, post_period_ms, precision, out_sensor);
}

static hsm_result_t CreatePeriodicSensorHandle(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_type_t type,
    int64_t post_period_ms,
    hsm_int_function_t function,
    hsm_int_values_function_t values_function,
    void* user_data,
    int32_t max_cache_size,
    hsm_sensor_t** out_sensor)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    if (post_period_ms <= 0)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreatePeriodicSensor(
        path, type, post_period_ms, function, values_function, user_data, max_cache_size, sensor);
    if (result != HSM_RESULT_OK)
        return result;

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

hsm_result_t hsm_collector_create_rate_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    hsm_sensor_t** out_sensor)
{
    return CreatePeriodicSensorHandle(collector, path, HSM_SENSOR_TYPE_RATE, post_period_ms, nullptr, nullptr, nullptr, 0, out_sensor);
}

hsm_result_t hsm_collector_create_function_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    hsm_int_function_t function,
    void* user_data,
    hsm_sensor_t** out_sensor)
{
    if (function == nullptr)
    {
        if (out_sensor != nullptr)
            *out_sensor = nullptr;

        return HSM_RESULT_INVALID_ARGUMENT;
    }

    return CreatePeriodicSensorHandle(collector, path, HSM_SENSOR_TYPE_INT, post_period_ms, function, nullptr, user_data, 0, out_sensor);
}

hsm_result_t hsm_collector_create_values_function_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    int32_t max_cache_size,
    hsm_int_values_function_t function,
    void* user_data,
    hsm_sensor_t** out_sensor)
{
    if (function == nullptr || max_cache_size <= 0)
    {
        if (out_sensor != nullptr)
            *out_sensor = nullptr;

        return HSM_RESULT_INVALID_ARGUMENT;
    }

    return CreatePeriodicSensorHandle(collector, path, HSM_SENSOR_TYPE_INT, post_period_ms, nullptr, function, user_data, max_cache_size, out_sensor);
}

hsm_result_t hsm_collector_create_file_sensor(
    hsm_collector_t* collector,
    const char* path,
    const char* default_file_name,
    const char* extension,
    hsm_sensor_t** out_sensor)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateFileSensor(path, default_file_name, extension, sensor);
    if (result != HSM_RESULT_OK)
        return result;

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

void hsm_sensor_release(hsm_sensor_t* sensor)
{
    delete sensor;
}

hsm_result_t hsm_sensor_add_int(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddInt(value, status, comment);
}

hsm_result_t hsm_sensor_add_bool(
    hsm_sensor_t* sensor,
    bool value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBool(value, status, comment);
}

hsm_result_t hsm_sensor_add_double(
    hsm_sensor_t* sensor,
    double value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddDouble(value, status, comment);
}

hsm_result_t hsm_sensor_add_string(
    hsm_sensor_t* sensor,
    const char* value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddString(value, status, comment);
}

hsm_result_t hsm_sensor_add_enum(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddEnum(value, status, comment);
}

hsm_result_t hsm_sensor_add_timespan(
    hsm_sensor_t* sensor,
    int64_t ticks,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddTimeSpan(ticks, status, comment);
}

hsm_result_t hsm_sensor_add_version(
    hsm_sensor_t* sensor,
    int32_t major,
    int32_t minor,
    int32_t build,
    int32_t revision,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddVersion(major, minor, build, revision, status, comment);
}

hsm_result_t hsm_sensor_add_bar_int(hsm_sensor_t* sensor, int32_t value)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBarInt(value);
}

hsm_result_t hsm_sensor_add_bar_double(hsm_sensor_t* sensor, double value)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBarDouble(value);
}

hsm_result_t hsm_sensor_add_bar_int_partial(
    hsm_sensor_t* sensor,
    int32_t min,
    int32_t max,
    int32_t mean,
    int32_t first,
    int32_t last,
    int32_t count)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBarIntPartial(min, max, mean, first, last, count);
}

hsm_result_t hsm_sensor_add_bar_double_partial(
    hsm_sensor_t* sensor,
    double min,
    double max,
    double mean,
    double first,
    double last,
    int32_t count)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddBarDoublePartial(min, max, mean, first, last, count);
}

hsm_result_t hsm_sensor_add_rate(
    hsm_sensor_t* sensor,
    double value,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddRate(value, status, comment);
}

hsm_result_t hsm_sensor_add_function_int(hsm_sensor_t* sensor, int32_t value)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddFunctionInt(value);
}

hsm_result_t hsm_sensor_add_file(
    hsm_sensor_t* sensor,
    const char* utf8_content,
    hsm_sensor_status_t status,
    const char* comment)
{
    if (sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AddFile(utf8_content, status, comment);
}

void hsm_collector_set_send_fail_next(hsm_collector_t* collector, int32_t count)
{
    if (collector != nullptr)
        collector->impl->SetSendFailNext(count);
}

void hsm_collector_set_send_hang(hsm_collector_t* collector, bool hang)
{
    if (collector != nullptr)
        collector->impl->SetSendHang(hang);
}

size_t hsm_collector_sent_count(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return 0;

    return collector->impl->SentCount();
}

hsm_result_t hsm_collector_get_sent_json(const hsm_collector_t* collector, size_t index, const char** out_json)
{
    if (out_json != nullptr)
        *out_json = nullptr;

    if (collector == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return collector->impl->SentJson(index, out_json);
}

const char* hsm_collector_last_error(const hsm_collector_t* collector)
{
    if (collector == nullptr)
        return "Collector handle is null.";

    return collector->impl->LastError();
}

// ---- Alert builders -----------------------------------------------------------------------------
// hsm_alert_t is an opaque alias for the collector-owned AlertData; the handle stays valid for the
// collector's lifetime (no separate release).
hsm_result_t hsm_collector_create_alert(
    hsm_collector_t* collector,
    hsm_alert_kind_t kind,
    hsm_alert_t** out_alert)
{
    if (out_alert != nullptr)
        *out_alert = nullptr;

    if (collector == nullptr || out_alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    *out_alert = reinterpret_cast<hsm_alert_t*>(collector->impl->CreateAlertData(kind));
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_add_condition(
    hsm_alert_t* alert,
    hsm_alert_combination_t combination,
    hsm_alert_property_t property,
    hsm_alert_operation_t operation,
    hsm_alert_target_type_t target_type,
    const char* target_value)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    AlertConditionData condition;
    condition.combination = combination;
    condition.property = property;
    condition.operation = operation;
    condition.target_type = target_type;
    // A LastValue target carries no comparand (serialized null); a Const target without an explicit
    // value also stays null (matches the managed DSL passing target?.ToString() == null).
    condition.target_is_null = target_type == HSM_ALERT_TARGET_LAST_VALUE || target_value == nullptr;
    condition.target_value = condition.target_is_null ? std::string{} : target_value;

    reinterpret_cast<AlertData*>(alert)->conditions.push_back(std::move(condition));
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_notification(
    hsm_alert_t* alert,
    const char* notification_template,
    hsm_alert_destination_mode_t destination)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_template = notification_template != nullptr;
    data->template_text = notification_template != nullptr ? notification_template : "";
    data->destination = destination;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_scheduled_notification(
    hsm_alert_t* alert,
    const char* notification_template,
    int64_t time_unix_ms,
    hsm_alert_repeat_mode_t repeat_mode,
    bool instant_send,
    hsm_alert_destination_mode_t destination)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_template = notification_template != nullptr;
    data->template_text = notification_template != nullptr ? notification_template : "";
    data->destination = destination;
    data->has_scheduled_time = true;
    data->scheduled_time_unix_ms = time_unix_ms;
    data->has_repeat = true;
    data->repeat = repeat_mode;
    data->has_instant_send = true;
    data->instant_send = instant_send;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_icon(hsm_alert_t* alert, hsm_alert_icon_t icon)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_icon = true; // managed AndSetIcon always sets Icon (empty string for an unknown enum)
    data->icon = AlertIconUtf8(icon);
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_icon_raw(hsm_alert_t* alert, const char* utf8_icon)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_icon = utf8_icon != nullptr;
    data->icon = utf8_icon != nullptr ? utf8_icon : "";
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_sensor_error(hsm_alert_t* alert)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    reinterpret_cast<AlertData*>(alert)->status = HSM_SENSOR_STATUS_ERROR;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_confirmation_period(hsm_alert_t* alert, int64_t period_ms)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_confirmation = true;
    data->confirmation_ms = period_ms;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_disabled(hsm_alert_t* alert, bool disabled)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    reinterpret_cast<AlertData*>(alert)->is_disabled = disabled;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_alert_set_inactivity_period(hsm_alert_t* alert, int64_t period_ms)
{
    if (alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    auto* data = reinterpret_cast<AlertData*>(alert);
    data->has_inactivity = true;
    data->inactivity_ms = period_ms;
    return HSM_RESULT_OK;
}

hsm_result_t hsm_sensor_attach_alert(hsm_sensor_t* sensor, hsm_alert_t* alert)
{
    if (sensor == nullptr || alert == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    return sensor->impl->AttachAlert(*reinterpret_cast<AlertData*>(alert));
}

// Test-only: the wire (System.Text.Json) registration payload of a built sensor, including any
// attached alerts — the byte source the native_wire_* unit tests pin against the .NET golden.
extern "C" const char* hsm_sensor_test_wire_registration_json(hsm_sensor_t* sensor)
{
    static thread_local std::string buffer;
    buffer = sensor != nullptr ? sensor->impl->WireRegistrationJson() : std::string{};
    return buffer.c_str();
}

// Test-only: one alert serialized as System.Text.Json renders AlertUpdateRequest.
extern "C" const char* hsm_alert_test_wire_json(hsm_alert_t* alert)
{
    static thread_local std::string buffer;
    buffer = alert != nullptr ? BuildAlertJson(*reinterpret_cast<AlertData*>(alert)) : std::string{};
    return buffer.c_str();
}

// Test-only: merge a prototype (identity is_computer + a TTL/description default) with custom
// overrides and return the merged internal registration text — pins the prototype-merge contract.
extern "C" const char* hsm_collector_test_merge_registration_json(
    int proto_is_computer,
    int64_t proto_ttl_ms,
    const char* proto_description,
    int64_t custom_ttl_ms,
    const char* custom_description,
    int custom_has_description,
    const char* path)
{
    static thread_local std::string buffer;

    RegistrationOptions prototype = InstantRegistrationDefaults();
    prototype.is_computer_sensor = proto_is_computer != 0;
    prototype.ttl_ms = proto_ttl_ms;
    prototype.description = proto_description != nullptr ? proto_description : "";

    RegistrationOptions custom; // a fresh "user options": has_description false unless set
    custom.ttl_ms = custom_ttl_ms;
    if (custom_has_description != 0)
    {
        custom.has_description = true;
        custom.description = custom_description != nullptr ? custom_description : "";
    }

    const auto merged = MergeRegistrationOptions(prototype, custom);
    buffer = BuildRegistrationJson(path != nullptr ? path : "", HSM_SENSOR_TYPE_INT, merged);
    return buffer.c_str();
}

// ---- Service-commands sensor --------------------------------------------------------------------
hsm_result_t hsm_collector_create_service_commands_sensor(hsm_collector_t* collector, hsm_sensor_t** out_sensor)
{
    if (out_sensor != nullptr)
        *out_sensor = nullptr;

    if (collector == nullptr || out_sensor == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    std::shared_ptr<NativeSensor> sensor;
    const auto result = collector->impl->CreateServiceCommandsSensor(sensor);
    if (result != HSM_RESULT_OK)
        return result;

    *out_sensor = new hsm_sensor_t{ std::move(sensor) };
    return HSM_RESULT_OK;
}

// A command posts the command string as the value with "Initiator: <initiator>" as the comment
// (SensorBase.SendValue default status Ok), matching ServiceCommandsSensor.SendCustomCommand.
hsm_result_t hsm_service_commands_send_custom(hsm_sensor_t* sensor, const char* command, const char* initiator)
{
    if (sensor == nullptr || command == nullptr)
        return HSM_RESULT_INVALID_ARGUMENT;

    const std::string comment = std::string("Initiator: ") + (initiator != nullptr ? initiator : "");
    return sensor->impl->AddString(command, HSM_SENSOR_STATUS_OK, comment.c_str());
}

hsm_result_t hsm_service_commands_send_restart(hsm_sensor_t* sensor, const char* initiator)
{
    return hsm_service_commands_send_custom(sensor, "Service restart", initiator);
}

hsm_result_t hsm_service_commands_send_start(hsm_sensor_t* sensor, const char* initiator)
{
    return hsm_service_commands_send_custom(sensor, "Service start", initiator);
}

hsm_result_t hsm_service_commands_send_stop(hsm_sensor_t* sensor, const char* initiator)
{
    return hsm_service_commands_send_custom(sensor, "Service stop", initiator);
}

hsm_result_t hsm_service_commands_send_update(hsm_sensor_t* sensor, const char* initiator)
{
    return hsm_service_commands_send_custom(sensor, "Service update", initiator);
}

hsm_result_t hsm_service_commands_send_update_version(
    hsm_sensor_t* sensor,
    const char* initiator,
    const char* new_version,
    const char* old_version)
{
    const std::string new_text = new_version != nullptr ? new_version : "";
    const bool has_old = old_version != nullptr && old_version[0] != '\0';
    const std::string command = has_old
                                    ? "Service update from " + std::string(old_version) + " to " + new_text
                                    : "Service update to " + new_text;
    return hsm_service_commands_send_custom(sensor, command.c_str(), initiator);
}
