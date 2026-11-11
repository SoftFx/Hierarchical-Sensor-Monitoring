#include "hsm_session_transport.hpp"

#include "hsm_ws_connection.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <random>

namespace hsm::http
{
    namespace
    {
        using Clock = std::chrono::steady_clock;

        constexpr const char* kNoSession = "no session";
        constexpr const char* kCancelled = "cancelled";
        constexpr const char* kPing = "{\"op\":\"ping\"}";

        // How long the session thread waits for an inbound frame before it looks at the outgoing
        // queue again. It is also the longest a request waits before its frame is written: the
        // connection has no wake-up for outgoing data. While requests are waiting for acks more
        // frames are likely to follow (a batch sent in several), so the wait is short then.
        constexpr int64_t kIdlePollMs = 50;
        constexpr int64_t kBusyPollMs = 5;
        // One frame must be written within this, or the session is given up.
        constexpr int64_t kSendTimeoutMs = 10000;

        constexpr int64_t kMinBackoffMs = 1000;
        constexpr int64_t kMaxBackoffMs = 60000;
        // A session that lasted this long was a working one: the next reconnect starts the
        // backoff again. One that ends sooner (the server takes the upgrade and then closes on
        // the first frame) keeps backing off, or it would be reopened every second for ever.
        constexpr int64_t kStableSessionMs = 60000;
        // An outage (no session, every open failing for a reason that may pass by itself) is
        // reported as an error once it has lasted this long. Five minutes is where the server
        // starts alerting on the collector's missing beat (docs/initiatives/collector-session-
        // websocket.md); a server restart or a CDN reshuffle is over well before it.
        constexpr int64_t kOutageErrorMs = 5 * 60000;
        // Text from the network or from libcurl is cut to this before it reaches a log line.
        constexpr size_t kMaxLoggedText = 300;
        // RFC 6455 5.5: a close frame carries at most 123 bytes of reason.
        constexpr size_t kMaxCloseReason = 123;
        // What Stop gives the session thread on top of the linger before it cancels the connection.
        constexpr int64_t kStopGraceMs = 1000;

        // Server limits outside these are not believed: a frame limit too small to carry a batch,
        // or a heartbeat that would spin or never fire.
        constexpr int64_t kMinFrameBytes = 1024;
        constexpr int64_t kMinHeartbeatSeconds = 1;
        constexpr int64_t kMaxHeartbeatSeconds = 3600;

        int64_t ElapsedMs(Clock::time_point since)
        {
            return std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - since).count();
        }

        // Typed reads that never throw: a field of another type is the same as a missing one.
        int64_t IntegerField(const nlohmann::json& object, const char* name)
        {
            const auto found = object.find(name);
            return found != object.end() && found->is_number_integer() ? found->get<int64_t>() : 0;
        }

        std::string StringField(const nlohmann::json& object, const char* name)
        {
            const auto found = object.find(name);
            return found != object.end() && found->is_string() ? found->get<std::string>() : std::string{};
        }

        std::string TwoDigits(int64_t value)
        {
            return (value < 10 ? "0" : "") + std::to_string(value);
        }

        // A duration for a log line: "850 ms", "12.3 s", "5m 03s", "3h 12m 05s".
        std::string FormatDuration(int64_t ms)
        {
            ms = (std::max<int64_t>)(ms, 0);
            if (ms < 1000)
                return std::to_string(ms) + " ms";
            if (ms < 60000)
                return std::to_string(ms / 1000) + "." + std::to_string(ms % 1000 / 100) + " s";
            const int64_t seconds = ms / 1000;
            if (seconds < 3600)
                return std::to_string(seconds / 60) + "m " + TwoDigits(seconds % 60) + "s";
            return std::to_string(seconds / 3600) + "h " + TwoDigits(seconds / 60 % 60) + "m " + TwoDigits(seconds % 60) + "s";
        }

        // Text that came from the network (or from libcurl, which may quote it), made safe for a
        // single log line: control characters and line breaks become '?', and it is cut to
        // `limit` bytes, so a peer can neither forge log lines nor flood one.
        std::string Printable(const std::string& text, size_t limit = kMaxLoggedText)
        {
            std::string out;
            const size_t size = (std::min)(text.size(), limit);
            out.reserve(size + 3);
            for (size_t i = 0; i < size; ++i)
            {
                const char c = text[i];
                out.push_back(static_cast<unsigned char>(c) < 0x20 || c == 0x7f ? '?' : c);
            }
            if (text.size() > limit)
                out += "...";
            return out;
        }

        // Refusals of the upgrade that a retry will not cure: a malformed or unauthorized
        // request, no such endpoint (sessions off), a key that is not accepted.
        bool IsPermanentRefusal(long status)
        {
            return status == 400 || status == 401 || status == 403 || status == 404 || status == 405 || status == 406 || status == 410 ||
                   status == 426;
        }

        // What makes two open failures "the same" for the once-per-reason rule: the HTTP status
        // and the text without its digits. libcurl words a timeout with a number that changes
        // from one attempt to the next ("after 15001 ms"), and a server that stays away must not
        // get a line per attempt for it.
        std::string FailureKey(long status, const std::string& why)
        {
            std::string key = std::to_string(status) + ':';
            for (const char c : why)
            {
                if (c < '0' || c > '9')
                    key.push_back(c);
            }
            return key;
        }

        // How the far side ended a session whose Receive returned Closed. The close frame, when
        // there was one, may come from an intermediary (a proxy or a CDN) that speaks for the server.
        std::string DescribeClosure(const WebSocketClosure& closure)
        {
            if (!closure.close_frame)
                return "the connection ended without a close frame (TCP closed by the server or an intermediary)";
            if (closure.code == 0)
                return "the server sent a close frame without a status code";
            return "the server sent a close frame (code " + std::to_string(closure.code) +
                   (closure.reason.empty() ? std::string(", no reason)") : ", reason \"" + Printable(closure.reason, kMaxCloseReason) + "\")");
        }
    } // namespace

    SessionTransport::SessionTransport(SessionOptions options)
        : options_(std::move(options))
    {
    }

    SessionTransport::~SessionTransport()
    {
        Stop(0);
    }

    void SessionTransport::Log(SessionLogLevel level, const std::string& message) const
    {
        if (!options_.log)
            return;
        try
        {
            options_.log(level, message);
        }
        catch (...)
        {
        }
    }

    void SessionTransport::Start()
    {
        std::string reason;
        if (!WebSocketConnection::Supported(&reason))
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                down_reason_ = "this collector cannot open a session: " + reason;
            }
            Log(SessionLogLevel::Error,
                "The collector session transport is selected, but it is unavailable here: " + reason +
                    ". Nothing will be delivered until the transport setting is changed or libcurl is updated.");
            return;
        }

        std::lock_guard<std::mutex> guard(mutex_);
        if (thread_.joinable())
            return;
        stop_ = false;
        finished_ = false;
        down_reason_ = "connecting";
        thread_ = std::thread([this] { Run(); });
    }

    void SessionTransport::Stop(int64_t linger_ms)
    {
        std::shared_ptr<WebSocketConnection> connecting;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            stop_ = true;
            linger_ms_ = linger_ms;
            // An open in progress can block for the whole open timeout; a served session notices
            // the stop within one poll and closes in an orderly way, so it is left alone.
            if (!up_)
                connecting = connection_;
        }
        cv_.notify_all();
        if (connecting)
            connecting->Cancel();

        if (!thread_.joinable())
            return;

        // A served session ends by itself within a poll and the close handshake. When it does not
        // (a frame being written to a peer that stopped reading), the wait is cut short: a stop
        // must not hang on the network.
        std::shared_ptr<WebSocketConnection> stuck;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            const auto budget = std::chrono::milliseconds((std::max<int64_t>)(linger_ms, 0) + kStopGraceMs);
            if (!cv_.wait_for(lock, budget, [this] { return finished_; }))
                stuck = connection_;
        }
        if (stuck)
            stuck->Cancel();
        thread_.join();
    }

    void SessionTransport::Cancel()
    {
        {
            std::lock_guard<std::mutex> guard(mutex_);
            cancelled_ = true;
        }
        cv_.notify_all();
    }

    void SessionTransport::ResetCancel()
    {
        std::lock_guard<std::mutex> guard(mutex_);
        cancelled_ = false;
    }

    bool SessionTransport::IsUp() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return up_;
    }

    size_t SessionTransport::MaxBodyBytes() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return up_ && max_frame_bytes_ > kEnvelopeRoom ? max_frame_bytes_ - kEnvelopeRoom : 0;
    }

    SessionResult SessionTransport::Request(const char* op, const std::string& body, int64_t timeout_ms)
    {
        SessionResult failed;
        std::shared_ptr<Pending> pending;

        // Built before the lock: a frame can be megabytes, and the session thread needs the lock
        // to complete acks. An id spent on a request that is then refused is harmless.
        const uint64_t id = next_id_.fetch_add(1, std::memory_order_relaxed);
        std::string frame = "{\"id\":" + std::to_string(id) + ",\"op\":\"" + op + "\",\"body\":";
        frame += body;
        frame += '}';

        std::unique_lock<std::mutex> lock(mutex_);
        if (cancelled_)
        {
            failed.error = kCancelled;
            return failed;
        }
        if (!up_)
        {
            failed.error = down_reason_.empty() ? std::string(kNoSession) : std::string(kNoSession) + ": " + down_reason_;
            return failed;
        }
        if (frame.size() > max_frame_bytes_)
        {
            failed.error = "the frame is larger than the session allows";
            return failed;
        }

        pending = std::make_shared<Pending>();
        pending_[id] = pending;
        outgoing_.emplace_back(id, std::move(frame));

        const auto deadline = Clock::now() + std::chrono::milliseconds((std::max<int64_t>)(timeout_ms, 0));
        cv_.wait_until(lock, deadline, [&] { return pending->done || cancelled_; });
        if (pending->done)
            return pending->result;

        // Gave up. The frame may be on the wire already, so its ack can still come: it must find
        // no one waiting, and after a timeout the session is reopened so that it cannot come at all.
        pending_.erase(id);
        outgoing_.erase(std::remove_if(outgoing_.begin(), outgoing_.end(), [id](const auto& entry) { return entry.first == id; }),
                        outgoing_.end());
        if (cancelled_)
        {
            failed.error = kCancelled;
        }
        else
        {
            failed.error = "no ack within " + std::to_string(timeout_ms) + " ms";
            reset_requested_ = true;
        }
        return failed;
    }

    void SessionTransport::FailAll(const std::string& why)
    {
        std::lock_guard<std::mutex> guard(mutex_);
        for (auto& entry : pending_)
        {
            entry.second->result = SessionResult{};
            entry.second->result.error = why;
            entry.second->done = true;
        }
        pending_.clear();
        outgoing_.clear();
    }

    bool SessionTransport::WaitBeforeRetry(int attempt)
    {
        // Exponential with jitter: between half of the step and the whole step, so a fleet that
        // lost its server together does not come back together.
        const int64_t step = (std::min)(kMaxBackoffMs, kMinBackoffMs << (std::min)(attempt, 10));
        thread_local std::mt19937_64 random{ std::random_device{}() };
        const int64_t delay = std::uniform_int_distribution<int64_t>(step / 2, step)(random);

        std::unique_lock<std::mutex> lock(mutex_);
        return !cv_.wait_for(lock, std::chrono::milliseconds(delay), [this] { return stop_; });
    }

    bool SessionTransport::ReadHello(WebSocketConnection& connection, int64_t timeout_ms, std::string& why, bool& permanent)
    {
        permanent = false;
        std::string text;
        std::string error;
        if (connection.Receive(text, timeout_ms, error) != WebSocketReceive::Text)
        {
            why = "no hello frame from the server" + (error.empty() ? std::string{} : ": " + error);
            return false;
        }

        const auto hello = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
        if (!hello.is_object() || StringField(hello, "op") != "hello" || !hello.contains("body") || !hello["body"].is_object())
        {
            why = "the first frame of the session is not a hello";
            permanent = true;
            return false;
        }

        const auto& body = hello["body"];
        const int64_t frame_bytes = IntegerField(body, "maxFrameBytes");
        const int64_t heartbeat_seconds = IntegerField(body, "heartbeatSeconds");
        if (frame_bytes < kMinFrameBytes || heartbeat_seconds < kMinHeartbeatSeconds || heartbeat_seconds > kMaxHeartbeatSeconds)
        {
            why = "the hello frame carries unusable limits";
            permanent = true;
            return false;
        }

        std::lock_guard<std::mutex> guard(mutex_);
        max_frame_bytes_ = static_cast<size_t>((std::min<int64_t>)(frame_bytes, static_cast<int64_t>(WebSocketConnection::kMaxMessageBytes)));
        heartbeat_ms_ = heartbeat_seconds * 1000;
        return true;
    }

    void SessionTransport::HandleFrame(const std::string& text, Counters& counters)
    {
        const auto frame = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
        // Anything that is not an ack is a push or a pong. This version acts on neither (a pong
        // is only counted); unknown ops are ignored by contract.
        if (!frame.is_object())
            return;
        if (!frame.contains("ack") || !frame["ack"].is_number_unsigned())
        {
            if (StringField(frame, "op") == "pong")
                ++counters.pongs;
            return;
        }

        ++counters.acks;
        const uint64_t id = frame["ack"].get<uint64_t>();

        SessionResult result;
        result.acked = true;
        result.status = static_cast<long>(IntegerField(frame, "status"));
        // The server's words end up in the collector's log lines about the failed send.
        result.error = Printable(StringField(frame, "error"));

        {
            std::lock_guard<std::mutex> guard(mutex_);
            const auto found = pending_.find(id);
            if (found == pending_.end())
                return; // its request gave up waiting
            found->second->result = std::move(result);
            found->second->done = true;
            pending_.erase(found);
        }
        cv_.notify_all();
    }

    std::string SessionTransport::Serve(WebSocketConnection& connection, Counters& counters)
    {
        auto last_out = Clock::now();

        for (;;)
        {
            std::deque<std::pair<uint64_t, std::string>> to_send;
            int64_t heartbeat_ms = 0;
            bool awaiting_acks = false;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (stop_)
                    return {};
                if (reset_requested_)
                {
                    reset_requested_ = false;
                    return "a request got no ack in time";
                }
                to_send.swap(outgoing_);
                heartbeat_ms = heartbeat_ms_;
                awaiting_acks = !pending_.empty();
            }

            std::string error;
            for (const auto& entry : to_send)
            {
                if (!connection.SendText(entry.second, kSendTimeoutMs, error))
                    return "sending a frame failed: " + error;
                ++counters.requests_sent;
                last_out = Clock::now();
            }

            std::string text;
            switch (connection.Receive(text, awaiting_acks ? kBusyPollMs : kIdlePollMs, error))
            {
            case WebSocketReceive::Text:
                counters.last_in = Clock::now();
                HandleFrame(text, counters);
                break;
            case WebSocketReceive::Timeout:
                break;
            case WebSocketReceive::Closed:
                return DescribeClosure(connection.LastClosure());
            case WebSocketReceive::Error:
                // libcurl's text (a reset, a TLS failure, a protocol violation it detected) or this
                // collector's own refusal of a frame (binary, oversized).
                return error.empty() ? std::string("the connection failed") : "the connection failed: " + error;
            }

            // The heartbeat: say something when nothing was sent for a whole interval, and give
            // the session up when the server has been silent for two and a half.
            if (ElapsedMs(last_out) >= heartbeat_ms)
            {
                if (!connection.SendText(kPing, kSendTimeoutMs, error))
                    return "sending a heartbeat failed: " + error;
                ++counters.pings_sent;
                last_out = Clock::now();
            }
            if (ElapsedMs(counters.last_in) >= heartbeat_ms * 5 / 2)
                return "the server sent nothing for two and a half heartbeats (" + FormatDuration(heartbeat_ms * 5 / 2) + ")";

            // One line, once, when a session opened while sessions kept ending early has held.
            if (counters.announce_when_stable && ElapsedMs(counters.served_since) >= kStableSessionMs)
            {
                counters.announce_when_stable = false;
                Log(SessionLogLevel::Info, "The collector session has held for " + FormatDuration(kStableSessionMs) + " after " +
                                               std::to_string(counters.short_sessions) + " short-lived session(s); " +
                                               std::to_string(counters.suppressed_lines) + " line(s) about them were suppressed.");
            }
        }
    }

    void SessionTransport::Run()
    {
        // The thread lives in someone else's process: nothing may escape it. A failure (out of
        // memory, mostly) ends the session at hand, not the transport: after a pause it connects
        // again.
        for (;;)
        {
            try
            {
                RunLoop();
                break;
            }
            catch (...)
            {
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    up_ = false;
                    down_reason_ = "the session thread failed";
                }
                FailAll("the session thread failed");
                cv_.notify_all();
                Log(SessionLogLevel::Error, "The collector session thread failed; it starts again in a minute.");
            }

            std::unique_lock<std::mutex> lock(mutex_);
            if (cv_.wait_for(lock, std::chrono::milliseconds(kMaxBackoffMs), [this] { return stop_; }))
                break;
        }

        {
            std::lock_guard<std::mutex> guard(mutex_);
            up_ = false;
            finished_ = true;
        }
        cv_.notify_all();
    }

    // What is logged. The collector is a library: the host's log is not ours to fill, and many
    // hosts page someone on ERROR. So there are lines only when the session changes state, and
    // the facts (peer, timings, counters) travel inside those lines:
    //  - INFO "The collector session is up ...": a session opened. Peer address, how long the
    //    open took and, after an outage, how many attempts failed over how long.
    //  - INFO "The collector session ended after ...": how it ended (a close frame with its code
    //    and reason, a TCP close without one, a transport error, or this collector's own
    //    decision), its lifetime and counters. Not an ERROR: behind a CDN or a proxy a
    //    long-lived connection is rotated now and then, and the reconnect follows at once.
    //  - "Cannot open a collector session ...": once per distinct reason. ERROR when a retry will
    //    not cure it (the upgrade refused with 400/401/403/404/406..., a server that does not
    //    speak the protocol): someone has to act. INFO otherwise (503, 429, connection refused,
    //    timeouts), plus one ERROR "No collector session for ..." when the outage reaches
    //    kOutageErrorMs.
    //  - Sessions that keep ending early (open, die, reopen): the first end line of a reason is
    //    logged, repeats with the same reason and the up lines in between are suppressed and
    //    counted, and one INFO line says when a session holds again and how much was left out.
    //    Together with the backoff, a flapping connection logs a bounded number of lines.
    //  - Nothing on a stop: the host asked for it, and its stop may be holding the collector's
    //    locks while the session thread finishes; no host callback is made from there.
    // Every call to the log callback is made without holding mutex_.
    void SessionTransport::RunLoop()
    {
        int attempt = 0;                // backoff step
        int failed = 0;                 // failed opens since the last session (or since Start)
        bool had_session = false;       // a session was up before: the outage started when it ended
        bool outage_reported = false;   // the outage ERROR was logged for the current outage
        auto down_since = Clock::now(); // when the current stretch without a session began
        std::string reported;           // the last open failure logged, so a server that stays away is logged once
        bool flapping = false;          // the previous session ended before kStableSessionMs
        std::string flap_reason;        // the end reason last logged for a short-lived session
        uint64_t short_sessions = 0;    // short-lived sessions in a row
        uint64_t suppressed_lines = 0;  // lines left out about them since the last one logged

        for (;;)
        {
            auto connection = std::make_shared<WebSocketConnection>(options_.verify_peer);
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (stop_)
                    return;
                connection_ = connection;
            }

            std::string why;
            bool permanent = false;
            const auto open_started = Clock::now();
            const auto opened = connection->Open(options_.url, options_.headers, kSubprotocol, options_.open_timeout_ms);
            bool up = opened.ok;
            if (!up)
            {
                why = "the upgrade failed" + (opened.status_code != 0 ? " with HTTP " + std::to_string(opened.status_code) : std::string{}) +
                      (opened.error.empty() ? std::string{} : ": " + opened.error);
                // A 101 that still failed: the server did not select the subprotocol.
                permanent = IsPermanentRefusal(opened.status_code) || opened.status_code == 101;
            }
            else
            {
                // The hello gets what the open left of the attempt's budget, and at least a second.
                up = ReadHello(*connection, (std::max<int64_t>)(options_.open_timeout_ms - ElapsedMs(open_started), 1000), why, permanent);
            }
            why = Printable(why);

            const std::string peer = opened.peer.empty() ? std::string("an unknown address") : opened.peer;
            if (up)
            {
                const int64_t open_ms = ElapsedMs(open_started);
                size_t frame_bytes = 0;
                int64_t heartbeat_ms = 0;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    up_ = true;
                    down_reason_.clear();
                    // A request that gave up on the previous session after that session had
                    // already ended must not end this one.
                    reset_requested_ = false;
                    frame_bytes = max_frame_bytes_;
                    heartbeat_ms = heartbeat_ms_;
                }

                Counters counters;
                // While sessions keep ending early the up lines are left out; not after an outage
                // that was reported as an error, whose end must be seen.
                if (flapping && !outage_reported)
                {
                    ++suppressed_lines;
                    counters.announce_when_stable = true;
                    counters.short_sessions = short_sessions;
                    counters.suppressed_lines = suppressed_lines;
                }
                else
                {
                    std::string line = "The collector session is up";
                    if (failed > 0)
                        line += " after " + std::to_string(failed) + " failed attempt(s) and " + FormatDuration(ElapsedMs(down_since)) +
                                " without a session";
                    else if (had_session)
                        line += " again, " + FormatDuration(ElapsedMs(down_since)) + " after the previous one ended";
                    line += ": connected to " + peer + " in " + FormatDuration(open_ms) + " (connect, TLS, upgrade and hello); frame limit " +
                            std::to_string(frame_bytes) + " bytes, heartbeat " + std::to_string(heartbeat_ms / 1000) + " s.";
                    Log(SessionLogLevel::Info, line);
                }
                failed = 0;
                outage_reported = false;

                counters.last_in = Clock::now(); // the hello
                counters.served_since = Clock::now();
                why = Printable(Serve(*connection, counters));
                const int64_t lived_ms = ElapsedMs(counters.served_since);
                const int64_t silent_ms = ElapsedMs(counters.last_in);
                const bool stable = lived_ms >= kStableSessionMs;
                if (stable)
                    attempt = 0;

                int64_t linger_ms = 0;
                bool stopping = false;
                size_t waiting = 0;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    up_ = false;
                    stopping = stop_;
                    linger_ms = linger_ms_;
                    down_reason_ = stopping ? "the collector is stopping" : why;
                    waiting = pending_.size();
                }
                FailAll(stopping ? "the collector is stopping" : "the session ended: " + why);
                cv_.notify_all();

                if (stopping)
                {
                    connection->Close(linger_ms);
                    return;
                }
                connection->Close(0);
                had_session = true;
                down_since = Clock::now();

                bool log_end = true;
                std::string suppressed_note;
                if (stable)
                {
                    // The session held: whatever was suppressed before it went into its "has
                    // held" line, or there was nothing.
                    flapping = false;
                    short_sessions = 0;
                    suppressed_lines = 0;
                    flap_reason.clear();
                    reported.clear();
                }
                else
                {
                    ++short_sessions;
                    if (flapping && why == flap_reason)
                    {
                        log_end = false;
                        ++suppressed_lines;
                    }
                    else
                    {
                        if (suppressed_lines > 0)
                            suppressed_note = " (" + std::to_string(suppressed_lines) + " line(s) about earlier short-lived sessions suppressed)";
                        flap_reason = why;
                        suppressed_lines = 0;
                    }
                    flapping = true;
                }

                if (log_end)
                    Log(SessionLogLevel::Info, "The collector session ended after " + FormatDuration(lived_ms) + ": " + why + ". Peer " + peer +
                                                   "; requests sent " + std::to_string(counters.requests_sent) + ", acks received " +
                                                   std::to_string(counters.acks) + ", still waiting for an ack " + std::to_string(waiting) +
                                                   "; pings sent " + std::to_string(counters.pings_sent) + ", pongs received " +
                                                   std::to_string(counters.pongs) + "; last frame from the server " + FormatDuration(silent_ms) +
                                                   " before the end. Reconnecting" +
                                                   (stable ? std::string{} : " with backoff: it lasted less than " + FormatDuration(kStableSessionMs)) +
                                                   suppressed_note + ".");
            }
            else
            {
                connection->Close(0);
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    if (stop_)
                        return;
                    down_reason_ = why;
                }
                ++failed;
                const int64_t down_ms = ElapsedMs(down_since);
                // The peer is not part of the reason: behind a CDN it changes from one attempt to the
                // next, and the same failure must still be logged once.
                const std::string context = "attempt " + std::to_string(failed) + ", " + FormatDuration(down_ms) + " without a session" +
                                            (opened.peer.empty() ? std::string{} : ", peer " + opened.peer);
                const std::string failure = FailureKey(opened.status_code, why);
                if (failure != reported)
                {
                    Log(permanent ? SessionLogLevel::Error : SessionLogLevel::Info,
                        "Cannot open a collector session (" + context + "): " + why +
                            (permanent ? ". The server refuses it for a reason a retry will not change; values stay queued, retrying."
                                       : ". Values stay queued; retrying."));
                    reported = failure;
                }
                if (!outage_reported && !permanent && down_ms >= kOutageErrorMs)
                {
                    Log(SessionLogLevel::Error, "No collector session for " + FormatDuration(down_ms) + " (" + std::to_string(failed) +
                                                    " failed attempt(s); the last: " + why + "). Values stay queued; retrying.");
                    outage_reported = true;
                }
                // A permanent refusal was logged as an error already; it counts as the outage report.
                outage_reported = outage_reported || permanent;
            }

            if (!WaitBeforeRetry(attempt++))
                return;
        }
    }
} // namespace hsm::http
