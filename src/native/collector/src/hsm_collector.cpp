#include "hsm_collector/hsm_collector.h"

#include <algorithm>
#include <atomic>
#include <charconv>
#include <chrono>
#include <cctype>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <exception>
#include <iomanip>
#include <initializer_list>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

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

    int64_t UnixTimeMilliseconds()
    {
        const auto now = std::chrono::system_clock::now().time_since_epoch();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    }

    // Monotonic clock for periodic scheduling and rate elapsed-time math (mirrors the C#
    // collector's Stopwatch-based scheduler/rate sensor; wall-clock jumps must not skew rates).
    int64_t SteadyMilliseconds()
    {
        const auto now = std::chrono::steady_clock::now().time_since_epoch();
        return std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    }

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

    // Portable registration-option subset mirroring the managed sensor options
    // (registration_contract.hsmtest). has_description=false => "Description":null —
    // managed instant creates default to "", every other sensor family defaults to null.
    struct RegistrationOptions
    {
        int64_t ttl_ms = 0;
        int32_t unit = -1;
        bool has_description = false;
        std::string description;
        bool has_enum_options = false;
        std::vector<EnumOptionData> enum_options;
    };

    RegistrationOptions InstantRegistrationDefaults()
    {
        RegistrationOptions options;
        options.has_description = true;
        return options;
    }

    // Canonical cross-language registration text — must stay byte-identical to the C#
    // harness RegistrationText (fixed field order; TTL in .NET ticks = ms * 10000).
    std::string BuildRegistrationJson(const std::string& path, hsm_sensor_type_t type, const RegistrationOptions& options)
    {
        std::string ttl_text = "null";
        if (options.ttl_ms > 0)
            ttl_text = "[" + std::to_string(options.ttl_ms * 10000) + "]";

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
               ",\"EnumOptions\":" + enums_text + "}";
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

    private:
        hsm_result_t AddValueJson(std::string value_json, hsm_sensor_status_t status, const char* comment);

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

        // Periodic (rate / function) state, guarded by mutex_.
        bool is_periodic_ = false;
        int64_t post_period_ms_ = 0;
        int64_t next_post_ms_ = 0;
        double rate_sum_ = 0.0;
        hsm_sensor_status_t rate_status_ = HSM_SENSOR_STATUS_OK;
        std::string rate_comment_;
        std::chrono::steady_clock::time_point rate_prev_{};
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
            }

            NotifyLifecycle(CollectorState::Starting);

            // Every start re-registers every sensor (mirrors C# InitAsync sending the
            // AddOrUpdate command per start). RegistrationJson is immutable, so reading
            // it under the collector lock keeps the one-way lock order. Map iteration
            // order is unspecified — multi-sensor fixtures assert counts only.
            std::vector<std::shared_ptr<NativeSensor>> sensors_snapshot;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                sensors_snapshot.reserve(sensors_.size());
                for (const auto& sensor : sensors_)
                {
                    sensors_snapshot.push_back(sensor.second);
                    registrations_.push_back(sensor.second->RegistrationJson());
                }
            }

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

            std::lock_guard<std::mutex> op_guard(op_mutex_);
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
            RegisterSensorLocked(sensor, type, sensor_path, RegistrationOptions{});
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
            RegisterSensorLocked(sensor, type, sensor_path, RegistrationOptions{});
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

        // Run a host-supplied callback so a throwing/crashing one can neither cross the C ABI
        // boundary nor break the collector (lifecycle listeners, log sinks, scheduler onError).
        template <typename Fn>
        static void InvokeIsolated(Fn&& fn) noexcept
        {
            try
            {
                fn();
            }
            catch (...)
            {
                // Swallowed by contract — see overview.md "Callback exception isolation".
            }
        }

        // Fire a lifecycle transition to every listener, exception-isolated. Caller holds
        // op_mutex_ (never mutex_), so callbacks observe transitions in order and a listener
        // may safely read collector state — but must not re-enter Start/Stop/Dispose.
        void NotifyLifecycle(CollectorState state)
        {
            const auto status = ToPublicStatus(state);
            for (const auto& listener : lifecycle_listeners_)
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
        // so a storm of identical messages collapses to one log line plus a periodic
        // "(N suppressed)" flush. A zero window logs immediately and returns (no dedup) — the
        // managed MessageDeduplicator's zero-window contract (and the double-log bug guard).
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

        // Caller holds mutex_. New sensors register immediately when the collector can start
        // new sensors (Starting/Running — mirrors C# InitAsync on dynamic creation); sensors
        // created before Start are registered by the Start loop.
        void RegisterSensorLocked(
            const std::shared_ptr<NativeSensor>& sensor,
            hsm_sensor_type_t type,
            const std::string& sensor_path,
            const RegistrationOptions& registration)
        {
            sensor->SetRegistrationJson(BuildRegistrationJson(sensor_path, type, registration));

            if (CanStartNewSensorsLocked())
                registrations_.push_back(sensor->RegistrationJson());
        }

        // FIFO send queue. Lock discipline: `queue_mutex_` and `mutex_` are never held together —
        // the enqueue path locks mutex_ (state check) then queue_mutex_ sequentially, and the
        // dispatcher locks queue_mutex_ (pop) then mutex_ (record) sequentially.
        void Enqueue(std::string json)
        {
            std::lock_guard<std::mutex> guard(queue_mutex_);

            queue_.push_back(std::move(json));

            // Eviction happens on the enqueueing thread: oldest-first, newest value kept —
            // same position-based FIFO policy as the C# QueueProcessorBase.
            while (queue_.size() > static_cast<size_t>(max_queue_size_))
                queue_.pop_front();
        }

        void StartWorker()
        {
            {
                std::lock_guard<std::mutex> guard(hang_mutex_);
                send_cancelled_ = false;
            }

            {
                std::lock_guard<std::mutex> guard(queue_mutex_);
                worker_stop_ = false;
            }

            worker_ = std::thread([this] { WorkerLoop(); });
        }

        void StartScheduler()
        {
            {
                std::lock_guard<std::mutex> guard(scheduler_mutex_);
                scheduler_stop_ = false;
            }

            scheduler_ = std::thread([this] { SchedulerLoop(); });
        }

        void StopScheduler()
        {
            {
                std::lock_guard<std::mutex> guard(scheduler_mutex_);
                scheduler_stop_ = true;
            }

            scheduler_cv_.notify_all();

            if (scheduler_.joinable())
                scheduler_.join();
        }

        // ~10ms tick granularity; each due periodic sensor builds its post under its own lock
        // and publishes through the Running gate. Sensor locks are taken strictly outside the
        // collector lock (snapshot first), same one-way order as everywhere else.
        void SchedulerLoop()
        {
            std::unique_lock<std::mutex> lock(scheduler_mutex_);

            while (!scheduler_stop_)
            {
                scheduler_cv_.wait_for(lock, std::chrono::milliseconds(10), [this] { return scheduler_stop_; });

                if (scheduler_stop_)
                    break;

                lock.unlock();
                TickPeriodicSensors();
                lock.lock();
            }
        }

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
                std::string json;
                if (sensor->TryBuildPeriodicJson(json))
                    EnqueueIfRunning(std::move(json));
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
            std::unique_lock<std::mutex> lock(queue_mutex_);
            DispatchQueuedLocked(lock, /*clear_remainder_on_failure=*/true);
        }

        // Pops and sends batches of up to max_values_in_package_ until the queue is empty or a
        // send fails. The lock is dropped around the send so enqueues never wait on dispatch.
        // On failure the batch is re-enqueued at the TAIL (the C# re-enqueue contract: a retried
        // package rotates behind later packages) — or, during the stop flush, everything left is
        // dropped so shutdown cannot hang on a failing transport.
        void DispatchQueuedLocked(std::unique_lock<std::mutex>& lock, bool clear_remainder_on_failure)
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
                        queue_.clear();
                    }
                    else
                    {
                        for (auto& json : batch)
                            queue_.push_back(std::move(json));
                    }

                    return;
                }
            }
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

            std::lock_guard<std::mutex> guard(mutex_);

            for (auto& json : batch)
                sent_values_.push_back(std::move(json));

            return true;
        }

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

        std::mutex scheduler_mutex_;
        std::condition_variable scheduler_cv_;
        bool scheduler_stop_ = false;
        std::thread scheduler_;

        // Lifecycle operations (Start/Stop/Dispose) serialize on this coarse lock so a
        // dispose racing a stop joins the in-flight transition rather than duplicating
        // it. Listeners are invoked under it (after state_ flips) for consistent order.
        std::mutex op_mutex_;
        std::vector<LifecycleListener> lifecycle_listeners_;

        // Pluggable log sink + error deduplicator (overview.md "error-handling").
        std::mutex logger_mutex_;
        hsm_log_callback_t log_callback_ = nullptr;
        void* log_user_data_ = nullptr;
        std::unordered_map<std::string, DedupEntry> dedup_;

        std::string access_key_;
        std::string server_address_;
        int32_t port_;
        std::string client_name_;
        std::string module_;
        std::string computer_name_;
        int32_t max_queue_size_;
        int32_t max_values_in_package_;
        int32_t collect_period_ms_;
        int32_t request_timeout_ms_;
        int32_t max_sensors_;
        bool allow_untrusted_certificate_;
        bool allow_plaintext_transport_;
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
        next_post_ms_ = SteadyMilliseconds();
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

            const auto now_ms = SteadyMilliseconds();
            if (now_ms < next_post_ms_)
                return false;

            // Re-anchoring to the actual tick time lets the cadence drift by up to the
            // scheduler granularity (~10ms) per post, unlike the managed fixed-rate schedule.
            // Benign for the rate VALUE (it divides by measured elapsed, not the period); a
            // real port that wants to match managed post timestamps should anchor to the
            // previous due time instead.
            next_post_ms_ = now_ms + post_period_ms_;

            if (type_ == HSM_SENSOR_TYPE_RATE)
            {
                const auto now = std::chrono::steady_clock::now();

                // Divide by the measured elapsed time since the previous post; the first sample
                // falls back to the configured period (C# #1102-E2 contract).
                double seconds = post_period_ms_ / 1000.0;
                if (rate_has_prev_)
                {
                    const auto elapsed = std::chrono::duration<double>(now - rate_prev_).count();
                    if (elapsed > 0)
                        seconds = elapsed;
                }

                rate_prev_ = now;
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
    return CreateSensor(collector, path, HSM_SENSOR_TYPE_ENUM, false, std::string{}, out_sensor);
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

    auto registration = InstantRegistrationDefaults();
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
