#include "hsm_session_table_channel.hpp"

#include "../hsm_reconnect_backoff.hpp"
#include "../hsm_session_transport.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cstring>
#include <limits>

namespace hsm::collector::tables
{
    namespace
    {
        using Clock = std::chrono::steady_clock;
        using hsm::http::AckedBinaryLink;
        using hsm::http::AckedLinkAck;
        using hsm::http::AckedLinkEnd;
        using hsm::http::AckedLinkOpen;
        using hsm::http::AckedLinkOptions;
        using hsm::http::AckedLinkPutResult;

        constexpr std::uint8_t kKindBlock = 1;
        constexpr std::uint8_t kKindProbe = 2;
        constexpr std::int64_t kMinFrameBytes = static_cast<std::int64_t>(kTableFrameHeaderBytes) + 64;
        // While the session is down, the owner looks for it this often.
        constexpr std::int64_t kSessionPollMs = 250;
        // An idle open connection is looked at this often (a new session, the server's frames).
        constexpr std::int64_t kIdleServiceMs = 1000;
        // A put waiting for a connection looks again this often.
        constexpr std::int64_t kLinkPollMs = 50;
        // The close of a stop, and of a connection that a new session replaces.
        constexpr std::int64_t kCloseLingerMs = 1000;
        // A block frame may wait unread on the server for its frame budget: its write is bounded by
        // ten heartbeats, at least this.
        constexpr std::int64_t kMinSendBoundMs = 10000;

        std::int64_t MsUntil(Clock::time_point when)
        {
            const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(when - Clock::now()).count();
            return left > 0 ? static_cast<std::int64_t>(left) : 0;
        }

        std::string Seconds(std::int64_t ms)
        {
            if (ms >= 60000 && ms % 60000 == 0)
                return std::to_string(ms / 60000) + " minutes";
            return std::to_string(ms / 1000) + "." + std::to_string(ms % 1000 / 100) + " s";
        }

        std::int32_t ReadInt32(const std::uint8_t* at)
        {
            std::uint32_t value = 0;
            for (int i = 3; i >= 0; --i)
                value = (value << 8) | at[i];
            std::int32_t signed_value = 0;
            std::memcpy(&signed_value, &value, sizeof value);
            return signed_value;
        }

        // The limits of the hello's blockLimits a block is checked against; false with `why` when the
        // block exceeds one. Read from the block's own header (rows, tables, decompressed length).
        bool ExceedsLimits(const std::vector<std::uint8_t>& frame, const TableBlockLimits& limits, std::string& why)
        {
            if (frame.size() < kTableFrameHeaderBytes + 44)
                return false;
            const std::uint8_t* block = frame.data() + kTableFrameHeaderBytes;
            const std::int32_t rows = ReadInt32(block + 8);
            const std::int32_t tables = ReadInt32(block + 12);
            const std::int32_t decompressed = ReadInt32(block + 36);
            if (rows > limits.max_rows)
                why = std::to_string(rows) + " rows, at most " + std::to_string(limits.max_rows);
            else if (tables > limits.max_tables)
                why = std::to_string(tables) + " tables, at most " + std::to_string(limits.max_tables);
            else if (decompressed > limits.max_decompressed_bytes)
                why = std::to_string(decompressed) + " bytes decompressed, at most " + std::to_string(limits.max_decompressed_bytes);
            return !why.empty();
        }

        TableBlockAck NotDelivered(std::string why, std::int64_t retry_after_ms = -1)
        {
            TableBlockAck ack;
            ack.outcome = TableBlockOutcome::NotDelivered;
            ack.error = std::move(why);
            ack.retry_after_ms = retry_after_ms;
            return ack;
        }
    } // namespace

    SessionTableChannel::SessionTableChannel(SessionTableChannelOptions options)
        : options_(std::move(options))
    {
    }

    SessionTableChannel::~SessionTableChannel() = default;

    void SessionTableChannel::Attach(hsm::http::SessionTransport* session)
    {
        std::unique_ptr<AckedBinaryLink> link;
        if (session != nullptr)
        {
            AckedLinkOptions link_options;
            link_options.url = session->Options().url;
            link_options.headers = session->Options().headers;
            link_options.verify_peer = session->Options().verify_peer;
            link_options.subprotocol = SessionTableChannelOptions::kSubprotocol;
            link_options.header_bytes = static_cast<std::uint32_t>(kTableFrameHeaderBytes);
            link_options.probe_kind = kKindProbe;
            link_options.open_timeout_ms = options_.open_timeout_ms;
            link_options.max_inbound_bytes = 1024 * 1024;
            link_options.ack_abandoned_for_tests = options_.ack_abandoned_for_tests;
            link = std::make_unique<AckedBinaryLink>(std::move(link_options));
        }
        {
            std::lock_guard<std::mutex> guard(state_mutex_);
            url_ = session != nullptr ? session->Options().url : std::string();
        }
        std::unique_ptr<AckedBinaryLink> old;
        {
            std::lock_guard<std::mutex> guard(link_mutex_);
            old = std::move(link_);
            link_ = std::move(link);
            if (link_ && cancelled_.load())
                link_->Cancel();
        }
        session_.store(session);
        serving_.store(false);
    }

    std::size_t SessionTableChannel::MaxBlockBytes() const
    {
        std::lock_guard<std::mutex> guard(limits_mutex_);
        return max_frame_bytes_ - kTableFrameHeaderBytes;
    }

    TableBlockLimits SessionTableChannel::Limits() const
    {
        std::lock_guard<std::mutex> guard(limits_mutex_);
        return limits_;
    }

    std::string SessionTableChannel::DownReason() const
    {
        std::lock_guard<std::mutex> guard(state_mutex_);
        return down_reason_;
    }

    std::string SessionTableChannel::Url() const
    {
        std::lock_guard<std::mutex> guard(state_mutex_);
        return url_;
    }

    void SessionTableChannel::SetDown(std::string reason)
    {
        std::lock_guard<std::mutex> guard(state_mutex_);
        down_reason_ = std::move(reason);
    }

    void SessionTableChannel::Report(const std::string& kind, std::string message, bool error)
    {
        std::lock_guard<std::mutex> guard(reports_mutex_);
        // Bounded: the kinds are few, but an outcome word is the server's.
        if (reported_kinds_.size() >= kMaxReportedKinds)
            reported_kinds_.clear();
        if (!reported_kinds_.insert(kind).second)
            return;
        reports_.emplace_back(error, std::move(message));
    }

    void SessionTableChannel::Info(std::string message)
    {
        std::lock_guard<std::mutex> guard(reports_mutex_);
        reports_.emplace_back(false, std::move(message));
    }

    void SessionTableChannel::WriteReports() noexcept
    {
        try
        {
            std::deque<std::pair<bool, std::string>> reports;
            {
                std::lock_guard<std::mutex> guard(reports_mutex_);
                reports.swap(reports_);
            }
            if (!options_.log)
                return;
            for (const auto& report : reports)
            {
                try
                {
                    options_.log(report.first, report.second);
                }
                catch (...)
                {
                }
            }
        }
        catch (...)
        {
        }
    }

    std::int64_t SessionTableChannel::Backoff()
    {
        const int attempt = attempt_++;
        if (options_.backoff_ms)
            return (std::max<std::int64_t>)(options_.backoff_ms(attempt), 0);
        return hsm::http::ReconnectBackoffDelayMs(attempt);
    }

    void SessionTableChannel::Cancel()
    {
        cancelled_.store(true);
        {
            std::lock_guard<std::mutex> guard(link_mutex_);
            if (link_)
                link_->Cancel();
        }
        if (auto* session = session_.load())
            session->Wake();
        {
            std::lock_guard<std::mutex> guard(wait_mutex_);
        }
        wait_cv_.notify_all();
    }

    void SessionTableChannel::Reset()
    {
        cancelled_.store(false);
        {
            std::lock_guard<std::mutex> guard(link_mutex_);
            if (link_)
                link_->ResetCancel();
        }
        // A run starts afresh: nothing is wanted until a registration of the run is accepted.
        wanted_ = false;
        attempt_ = 0;
        next_attempt_at_ = Clock::time_point{};
        lift_with_next_session_ = false;
        refused_status_ = 0;
        refused_session_.clear();
        no_id_generation_ = 0;
        serving_.store(false);
        SetDown("the table connection is not open yet");
    }

    bool SessionTableChannel::WaitFor(Clock::time_point until)
    {
        std::unique_lock<std::mutex> lock(wait_mutex_);
        wait_cv_.wait_until(lock, until, [this] { return cancelled_.load(); });
        return !cancelled_.load();
    }

    // ---- Keeping the connection -------------------------------------------------------------------------

    bool SessionTableChannel::HelloCheck(const std::string& body, std::string& why)
    {
        const auto hello = nlohmann::json::parse(body, nullptr, /*allow_exceptions=*/false);
        const auto frame = hello.is_object() ? hello.find("maxFrameBytes") : hello.end();
        const std::int64_t frame_bytes = frame != hello.end() && frame->is_number_integer() ? frame->get<std::int64_t>() : 0;
        if (frame_bytes <= kMinFrameBytes || frame_bytes > (std::numeric_limits<std::int32_t>::max)())
        {
            why = "the table hello states a frame limit this collector cannot work with (maxFrameBytes " + std::to_string(frame_bytes) + ")";
            return false;
        }

        TableBlockLimits limits;
        const auto stated = hello.find("blockLimits");
        if (stated != hello.end() && !stated->is_null())
        {
            const auto read = [&](const char* name, auto& field) {
                const auto value = stated->is_object() ? stated->find(name) : stated->end();
                if (value == stated->end() || !value->is_number_integer())
                    return false;
                const std::int64_t number = value->get<std::int64_t>();
                using Field = std::remove_reference_t<decltype(field)>;
                if (number < static_cast<std::int64_t>((std::numeric_limits<Field>::min)()) || number > static_cast<std::int64_t>((std::numeric_limits<Field>::max)()))
                    return false;
                field = static_cast<Field>(number);
                return true;
            };
            if (!stated->is_object() || !read("maxDecompressedBytes", limits.max_decompressed_bytes) || !read("maxTables", limits.max_tables) ||
                !read("maxRows", limits.max_rows) || !read("maxColumnsPerTable", limits.max_columns_per_table) ||
                !read("maxTableIdLength", limits.max_table_id_length) || !limits.Valid())
            {
                why = "the table hello states block limits this collector cannot work with";
                return false;
            }
        }

        // Kept for the pipeline: the next pack splits by the server's numbers.
        std::lock_guard<std::mutex> guard(limits_mutex_);
        max_frame_bytes_ = static_cast<std::size_t>(frame_bytes);
        limits_ = limits;
        return true;
    }

    bool SessionTableChannel::TryOpen(std::uint64_t generation, const std::string& session_id)
    {
        const auto now = Clock::now();
        if (generation == 0)
        {
            SetDown("no collector session");
            return false;
        }
        if (session_id.empty())
        {
            // A server before table sensors: no attempt with this session; the next one may say more.
            if (no_id_generation_ != generation)
            {
                no_id_generation_ = generation;
                SetDown("the server's session states no sessionId: the server has no table connections");
                Report("open:no-session-id", "Cannot open the table connection to " + Url() +
                                                 ": the server's session states no sessionId: the server has no table connections. Table blocks are kept "
                                                 "(max_backlog_bytes, the oldest dropped and counted); asking again with every new session.");
            }
            return false;
        }
        // A 409 for this session: the next session is the next chance.
        if (session_id == refused_session_)
            return false;
        // Refused for good (404, 400): asked again after the long pause or with the next session.
        if ((refused_status_ == 404 || refused_status_ == 400) && refused_generation_ == generation &&
            now < refused_at_ + std::chrono::milliseconds(options_.refused_retry_ms))
            return false;
        if (now < next_attempt_at_ && !(lift_with_next_session_ && generation != backoff_generation_))
            return false;

        AckedBinaryLink* link = link_.get();
        if (link == nullptr)
            return false;

        attempts_.fetch_add(1);
        const AckedLinkOpen opened = link->Open(session_id, [this](const std::string& body, std::string& why) { return HelloCheck(body, why); });
        if (opened.ok)
        {
            refused_status_ = 0;
            refused_session_.clear();
            lift_with_next_session_ = false;
            link_generation_ = generation;
            serving_since_ = Clock::now();
            opened_.fetch_add(1);
            serving_.store(true);
            SetDown(std::string());
            {
                // A failure that comes back after this connection is news again.
                std::lock_guard<std::mutex> guard(reports_mutex_);
                reported_kinds_.clear();
            }
            Info("The table connection is up: frame limit " + std::to_string(MaxBlockBytes() + kTableFrameHeaderBytes) + " bytes, heartbeat " +
                 std::to_string(link->HeartbeatMs() / 1000) + " s.");
            return true;
        }
        if (cancelled_.load())
            return false;

        SetDown(opened.why);
        ReportOpenFailure(opened, session_id);
        switch (opened.status)
        {
        case 404:
        case 400:
            refused_status_ = opened.status;
            refused_at_ = now;
            refused_generation_ = generation;
            return false;
        case 409:
            refused_session_ = session_id;
            return false;
        default:
            break;
        }
        lift_with_next_session_ = false;
        next_attempt_at_ = now + std::chrono::milliseconds(Backoff());
        return false;
    }

    // Said once for a kind of failure (the step and the status or libcurl's code, AckedLinkOpen::kind),
    // with the words of the first one: libcurl's text carries elapsed times, so the same timeout behind
    // a proxy would otherwise be a new line at every attempt.
    void SessionTableChannel::ReportOpenFailure(const AckedLinkOpen& opened, const std::string& session_id)
    {
        const std::string kept = "Table blocks are kept (max_backlog_bytes, the oldest dropped and counted)";
        const std::string kind = "open:" + (opened.kind.empty() ? "status:" + std::to_string(opened.status) : opened.kind);
        switch (opened.status)
        {
        case 404:
            Report(kind, "The server refused the table connection with 404: table connections are switched off on " + Url() +
                             " (CollectorSessions.MaxTableFrameBytes = 0). " + kept + "; asking again every " + Seconds(options_.refused_retry_ms) +
                             " and with the next session.");
            return;
        case 400:
            Report(kind, "The server refused the table connection with 400: " + Url() + " does not know the subprotocol " +
                             SessionTableChannelOptions::kSubprotocol + " (a server older than table sensors). " + kept + "; asking again every " +
                             Seconds(options_.refused_retry_ms) + " and with the next session.");
            return;
        case 409:
            Report(kind, "The server refused the table connection with 409: it knows no live session " + session_id +
                             " of this key. Table blocks are kept; waiting for the next session.");
            return;
        case 406:
            Report(kind, "The server refused the table connection with 406: the access key cannot send. Table blocks are kept; retrying.");
            return;
        case 503:
            Report(kind, "The server refused the table connection with 503: it is shutting down. Table blocks are kept; retrying.");
            return;
        default:
            Report(kind, "Cannot open the table connection to " + Url() + ": " + opened.why + ". Table blocks are kept; retrying.");
            return;
        }
    }

    void SessionTableChannel::HandleEnd(const AckedLinkEnd& end, bool session_changed)
    {
        serving_.store(false);
        const auto now = Clock::now();
        const std::int64_t lived = std::chrono::duration_cast<std::chrono::milliseconds>(now - serving_since_).count();
        if (lived >= kStableConnectionMs)
            attempt_ = 0;

        SetDown("the table connection ended: " + end.why);
        Info("The table connection ended after " + Seconds(lived) + ": " + end.why + ".");
        if (end.protocol_error)
            Report("closed:" + std::to_string(end.close_code), "The table connection to " + Url() + " was closed: " + end.why + ". Table blocks are kept; reconnecting.");

        if (session_changed)
        {
            // Its session ended: the next one is the one to name, at once.
            next_attempt_at_ = Clock::time_point{};
            lift_with_next_session_ = false;
            return;
        }
        // 1000 from the server: replaced, or its session ended (which this collector may not have
        // noticed yet). The next attempt waits for a new session or a backoff, whichever is first.
        lift_with_next_session_ = end.server_close == 1000;
        backoff_generation_ = link_generation_;
        next_attempt_at_ = now + std::chrono::milliseconds(Backoff());
    }

    bool SessionTableChannel::Maintain()
    {
        if (cancelled_.load())
            return false;
        auto* session = session_.load();
        AckedBinaryLink* link = link_.get();
        if (session == nullptr || link == nullptr)
        {
            SetDown("no collector session");
            return false;
        }

        const hsm::http::SessionIdentity identity = session->CurrentSession();
        if (link->IsOpen())
        {
            if (identity.generation != link_generation_)
            {
                // The session this connection names is gone: the new one gets a connection of its own.
                link->Close(kCloseLingerMs, 1000, "its session ended");
                AckedLinkEnd end;
                end.why = identity.generation == 0 ? "its session ended" : "a new session replaced its session";
                end.close_code = 1000;
                HandleEnd(end, /*session_changed=*/true);
            }
            else if (link->Service())
            {
                return true;
            }
            else
            {
                HandleEnd(link->LastEnd(), /*session_changed=*/false);
            }
        }

        if (!wanted_)
            return false;
        return TryOpen(identity.generation, identity.session_id);
    }

    std::int64_t SessionTableChannel::ServiceDelayMs()
    {
        if (cancelled_.load() || !wanted_)
            return -1;
        AckedBinaryLink* link = link_.get();
        if (link == nullptr)
            return -1;
        if (link->IsOpen())
            return (std::min)(MsUntil(link->NextPingDue()), kIdleServiceMs);
        auto* session = session_.load();
        if (session == nullptr)
            return -1;
        const auto identity = session->CurrentSession();
        if (identity.generation == 0)
            return kSessionPollMs;
        const auto now = Clock::now();
        if (identity.session_id.empty() || identity.session_id == refused_session_)
            return kIdleServiceMs;
        if ((refused_status_ == 404 || refused_status_ == 400) && refused_generation_ == identity.generation)
            return (std::min)(MsUntil(refused_at_ + std::chrono::milliseconds(options_.refused_retry_ms)), kIdleServiceMs);
        if (now < next_attempt_at_ && !(lift_with_next_session_ && identity.generation != backoff_generation_))
            return (std::min)(MsUntil(next_attempt_at_), lift_with_next_session_ ? kSessionPollMs : kIdleServiceMs);
        return 0;
    }

    bool SessionTableChannel::Service()
    {
        try
        {
            // True whenever this call opened a connection, also when it replaced the connection of an
            // ended session with one of the new session: the blocks that waited go at once.
            const std::int64_t opened_before = opened_.load();
            const bool open = Maintain();
            return open && opened_.load() > opened_before;
        }
        catch (...)
        {
            return false;
        }
    }

    bool SessionTableChannel::WaitForLink(Clock::time_point deadline)
    {
        for (;;)
        {
            if (Maintain())
                return true;
            if (cancelled_.load() || Clock::now() >= deadline)
                return false;
            const auto next = Clock::now() + std::chrono::milliseconds(kLinkPollMs);
            if (!WaitFor((std::min)(next, deadline)))
                return false;
        }
    }

    void SessionTableChannel::Close(std::int64_t linger_ms) noexcept
    {
        try
        {
            if (AckedBinaryLink* link = link_.get(); link != nullptr && link->IsOpen())
                link->Close(linger_ms, 1000, "collector stopped");
            serving_.store(false);
            SetDown("the table connection is stopped");
        }
        catch (...)
        {
        }
    }

    // ---- Registrations ----------------------------------------------------------------------------------

    TableRegistrationResult SessionTableChannel::Register(const std::vector<TableRegistration>& registrations)
    {
        TableRegistrationResult result;
        try
        {
            auto* session = session_.load();
            if (cancelled_.load())
            {
                result.unsettled_reason = "cancelled";
                return result;
            }
            // The run's first registration is asked for at Start, when the session is usually still
            // opening: it waits for the session as a put waits for its connection (the managed pipeline
            // waits until the channel is ready), so a start is not reported as a failed registration.
            std::size_t room = session != nullptr ? session->MaxBodyBytes() : 0;
            const auto deadline = Clock::now() + std::chrono::milliseconds(options_.connection_wait_ms);
            while (session != nullptr && room == 0 && Clock::now() < deadline &&
                   WaitFor((std::min)(Clock::now() + std::chrono::milliseconds(kLinkPollMs), deadline)))
                room = session->MaxBodyBytes();
            if (cancelled_.load())
            {
                result.unsettled_reason = "cancelled";
                return result;
            }
            if (room == 0)
            {
                result.unsettled_reason = "no collector session";
                return result;
            }

            std::size_t index = 0;
            while (index < registrations.size())
            {
                std::string body = "[";
                std::vector<std::string> paths;
                while (index < registrations.size())
                {
                    const std::string element = options_.registration_json(registrations[index]);
                    // A table registration is a few hundred bytes; one that does not fit a frame on its
                    // own is refused here, for good, as the server would refuse a frame over its limit.
                    if (element.size() + 2 > room)
                    {
                        result.refused[registrations[index].path] = "its registration of " + std::to_string(element.size()) +
                                                                    " bytes is larger than a collector session frame (" + std::to_string(room) + " bytes)";
                        ++index;
                        continue;
                    }
                    if (body.size() + element.size() + 1 + (paths.empty() ? 0 : 1) > room)
                        break;
                    if (!paths.empty())
                        body += ',';
                    body += element;
                    paths.push_back(registrations[index].path);
                    ++index;
                }
                if (paths.empty())
                    continue;
                body += ']';

                const auto ack = session->Request("commands", body, options_.request_timeout_ms, cancelled_);
                if (ack.IsSuccess())
                {
                    std::map<std::string, std::string> errors;
                    const auto parsed = nlohmann::json::parse(ack.body, nullptr, /*allow_exceptions=*/false);
                    if (parsed.is_object())
                    {
                        for (auto it = parsed.begin(); it != parsed.end(); ++it)
                            errors[it.key()] = it.value().is_string() ? it.value().get<std::string>() : std::string("refused");
                    }
                    for (const auto& path : paths)
                    {
                        const auto error = errors.find(path);
                        if (error != errors.end())
                            result.refused[path] = error->second;
                        else
                            result.accepted.insert(path);
                    }
                    continue;
                }

                // A 4xx is final for the whole request; a 5xx or no ack leaves the paths unsettled,
                // and the frames after it are tried all the same.
                if (ack.acked && ack.status >= 400 && ack.status < 500)
                {
                    for (const auto& path : paths)
                        result.refused[path] = "the server refused the registration request with status " + std::to_string(ack.status) +
                                               (ack.error.empty() ? std::string() : ": " + ack.error);
                    continue;
                }
                result.unsettled_reason = ack.acked ? "status " + std::to_string(ack.status) + (ack.error.empty() ? std::string() : ": " + ack.error) : ack.error;
            }

            // A table the server knows: its blocks need the table connection, opened now.
            if (!result.accepted.empty())
            {
                wanted_ = true;
                Maintain();
            }
        }
        catch (...)
        {
            result.unsettled_reason = "the table channel failed";
        }
        return result;
    }

    // ---- Blocks -----------------------------------------------------------------------------------------

    TableBlockAck SessionTableChannel::Put(const TableProducerId& producer, std::int64_t sequence, std::vector<std::uint8_t>& frame)
    {
        try
        {
            if (cancelled_.load())
                return NotDelivered("cancelled");
            if (frame.size() < kTableFrameHeaderBytes)
            {
                TableBlockAck invalid;
                invalid.outcome = TableBlockOutcome::Invalid;
                invalid.error = "a frame shorter than its header";
                return invalid;
            }

            // The rest of the header is the table connection's: the producer and its sequence.
            std::memcpy(frame.data() + 16, producer.data(), producer.size());
            for (std::size_t i = 0; i < 8; ++i)
                frame[32 + i] = static_cast<std::uint8_t>((static_cast<std::uint64_t>(sequence) >> (8 * i)) & 0xFF);

            wanted_ = true;
            for (int connection = 0; connection < kConnectionsPerPut; ++connection)
            {
                if (!WaitForLink(Clock::now() + std::chrono::milliseconds(options_.connection_wait_ms)))
                {
                    if (cancelled_.load())
                        return NotDelivered("cancelled");
                    const std::string down = DownReason();
                    return NotDelivered(down.empty() ? std::string("no table connection") : down);
                }

                // Judged by the hello of the connection the block goes on.
                {
                    std::size_t max_frame = 0;
                    TableBlockLimits limits;
                    {
                        std::lock_guard<std::mutex> guard(limits_mutex_);
                        max_frame = max_frame_bytes_;
                        limits = limits_;
                    }
                    TableBlockAck too_large;
                    too_large.outcome = TableBlockOutcome::TooLarge;
                    if (frame.size() > max_frame)
                    {
                        too_large.error = "a block of " + std::to_string(frame.size()) + " bytes with its frame header is larger than the table connection takes (" +
                                          std::to_string(max_frame) + " bytes)";
                        return too_large;
                    }
                    std::string exceeded;
                    if (ExceedsLimits(frame, limits, exceeded))
                    {
                        too_large.error = "a block exceeds the block limits of the table connection (" + exceeded + ")";
                        return too_large;
                    }
                }

                AckedBinaryLink* link = link_.get();
                const std::int64_t send_bound =
                    options_.send_bound_ms_for_tests > 0 ? options_.send_bound_ms_for_tests : (std::max)(kMinSendBoundMs, link->HeartbeatMs() * 10);
                AckedLinkAck ack;
                switch (link->Put(kKindBlock, frame, send_bound, options_.request_timeout_ms, ack))
                {
                case AckedLinkPutResult::Acked:
                    return Answer(ack);
                case AckedLinkPutResult::Cancelled:
                    serving_.store(false);
                    return NotDelivered("cancelled");
                case AckedLinkPutResult::Lost:
                case AckedLinkPutResult::NotOpen:
                    // The same frame goes on the next connection: same producer, same sequence.
                    HandleEnd(link->LastEnd(), /*session_changed=*/false);
                    break;
                }
            }
            return NotDelivered("the table connection was lost " + std::to_string(kConnectionsPerPut) +
                                " times while the block waited for its ack (" + DownReason() + ")");
        }
        catch (...)
        {
            return NotDelivered("the table channel failed");
        }
    }

    // The ack of a block as the pipeline's answer. For each status: can a repeat cure it? A refusal a
    // repeat cannot cure is final; a fault is never final.
    TableBlockAck SessionTableChannel::Answer(const AckedLinkAck& ack)
    {
        const long status = ack.status;
        if (status >= 200 && status < 300)
            return ReadOutcome(ack);

        const std::int64_t retry_after = ack.retry_after_seconds > 0 ? (std::min)(ack.retry_after_seconds * 1000, kMaxRetryAfterMs) : -1;
        const std::string words = ack.error.empty() ? "status " + std::to_string(status) : "status " + std::to_string(status) + ": " + ack.error;

        // A request that waited too long, or a rate limit: a wait cures it.
        if (status == 408 || status == 429)
            return NotDelivered("the server answered a block with " + words, retry_after);
        // 400 and every other refusal of the request: the same frame would be refused again.
        if (status >= 400 && status < 500)
        {
            TableBlockAck invalid;
            invalid.outcome = TableBlockOutcome::Invalid;
            invalid.error = words;
            return invalid;
        }
        // 500, 503 and every other fault of the server: nothing was stored, the same frame goes again.
        if (status >= 500 && status < 600)
            return NotDelivered("the server answered a block with " + words, retry_after);

        // A status no version of the protocol sends: the connection is not trusted any more.
        const std::string why = "the server answered a block with status " + std::to_string(status);
        if (AckedBinaryLink* link = link_.get(); link != nullptr && link->IsOpen())
        {
            link->Close(kCloseLingerMs, 1002, why);
            AckedLinkEnd end;
            end.why = why;
            end.protocol_error = true;
            end.close_code = 1002;
            HandleEnd(end, /*session_changed=*/false);
        }
        return NotDelivered(why);
    }

    TableBlockAck SessionTableChannel::ReadOutcome(const AckedLinkAck& ack)
    {
        TableBlockAck answer;
        answer.outcome = TableBlockOutcome::Stored;
        std::string outcome;
        const auto body = nlohmann::json::parse(ack.body, nullptr, /*allow_exceptions=*/false);
        if (body.is_discarded())
        {
            // A 200 settles the block whatever its body says; the refusals it lists are then unknown.
            Report("ack-body:not-json", "The body of a table block ack is not JSON; the block is taken as stored.", /*error=*/false);
            return answer;
        }
        else if (body.is_object())
        {
            if (const auto found = body.find("outcome"); found != body.end() && found->is_string())
                outcome = found->get<std::string>();
            if (const auto found = body.find("refusedCount"); found != body.end() && found->is_number_integer())
            {
                const std::int64_t count = found->get<std::int64_t>();
                answer.refused_count = static_cast<std::int32_t>((std::max<std::int64_t>)((std::min<std::int64_t>)(count, (std::numeric_limits<std::int32_t>::max)()), 0));
            }
            if (const auto found = body.find("refused"); found != body.end() && found->is_array())
            {
                for (const auto& entry : *found)
                {
                    if (!entry.is_object())
                        continue;
                    TableRefusal refusal;
                    if (const auto index = entry.find("index"); index != entry.end() && index->is_number_integer())
                    {
                        const std::int64_t value = index->get<std::int64_t>();
                        refusal.index = value >= 0 && value <= (std::numeric_limits<std::int32_t>::max)() ? static_cast<std::int32_t>(value) : -1;
                    }
                    if (const auto table = entry.find("table"); table != entry.end() && table->is_string())
                        refusal.table = AckedBinaryLink::Printable(table->get<std::string>());
                    const auto reason = entry.find("reason");
                    refusal.reason = reason != entry.end() && reason->is_string() ? AckedBinaryLink::Printable(reason->get<std::string>()) : "unknown";
                    answer.refused.push_back(std::move(refusal));
                }
            }
        }

        if (outcome == "stored")
            answer.outcome = TableBlockOutcome::Stored;
        else if (outcome == "duplicate")
            answer.outcome = TableBlockOutcome::Duplicate; // "refusedKnown": false carries no list
        else if (outcome == "allRefused")
            answer.outcome = TableBlockOutcome::AllRefused;
        else
        {
            // A 200 settles the block (wire contract); an outcome this version does not know is said
            // and taken as stored, so the block does not hold the ones behind it for ever.
            Report("ack-outcome:" + AckedBinaryLink::Printable(outcome, 64), "The server answered a table block with an outcome this collector does not know (" +
                                                                                 (outcome.empty() ? std::string("none") : AckedBinaryLink::Printable(outcome)) + "); the block is taken as stored.");
            answer.outcome = TableBlockOutcome::Stored;
        }
        return answer;
    }
} // namespace hsm::collector::tables
