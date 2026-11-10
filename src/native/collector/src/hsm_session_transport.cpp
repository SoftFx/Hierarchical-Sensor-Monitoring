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

    bool SessionTransport::ReadHello(WebSocketConnection& connection, int64_t timeout_ms, std::string& why)
    {
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
            return false;
        }

        const auto& body = hello["body"];
        const int64_t frame_bytes = IntegerField(body, "maxFrameBytes");
        const int64_t heartbeat_seconds = IntegerField(body, "heartbeatSeconds");
        if (frame_bytes < kMinFrameBytes || heartbeat_seconds < kMinHeartbeatSeconds || heartbeat_seconds > kMaxHeartbeatSeconds)
        {
            why = "the hello frame carries unusable limits";
            return false;
        }

        std::lock_guard<std::mutex> guard(mutex_);
        max_frame_bytes_ = static_cast<size_t>((std::min<int64_t>)(frame_bytes, static_cast<int64_t>(WebSocketConnection::kMaxMessageBytes)));
        heartbeat_ms_ = heartbeat_seconds * 1000;
        return true;
    }

    void SessionTransport::HandleFrame(const std::string& text)
    {
        const auto frame = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
        // Anything that is not an ack is a push or a pong. This version acts on neither; unknown
        // ops are ignored by contract.
        if (!frame.is_object() || !frame.contains("ack") || !frame["ack"].is_number_unsigned())
            return;

        const uint64_t id = frame["ack"].get<uint64_t>();

        SessionResult result;
        result.acked = true;
        result.status = static_cast<long>(IntegerField(frame, "status"));
        result.error = StringField(frame, "error");

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

    std::string SessionTransport::Serve(WebSocketConnection& connection)
    {
        auto last_in = Clock::now();
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
                last_out = Clock::now();
            }

            std::string text;
            switch (connection.Receive(text, awaiting_acks ? kBusyPollMs : kIdlePollMs, error))
            {
            case WebSocketReceive::Text:
                last_in = Clock::now();
                HandleFrame(text);
                break;
            case WebSocketReceive::Timeout:
                break;
            case WebSocketReceive::Closed:
                return "the server closed the session";
            case WebSocketReceive::Error:
                return error.empty() ? std::string("the connection failed") : error;
            }

            // The heartbeat: say something when nothing was sent for a whole interval, and give
            // the session up when the server has been silent for two and a half.
            if (ElapsedMs(last_out) >= heartbeat_ms)
            {
                if (!connection.SendText(kPing, kSendTimeoutMs, error))
                    return "sending a heartbeat failed: " + error;
                last_out = Clock::now();
            }
            if (ElapsedMs(last_in) >= heartbeat_ms * 5 / 2)
                return "the server sent nothing for two and a half heartbeats";
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

    void SessionTransport::RunLoop()
    {
        int attempt = 0;
        std::string reported; // the last reason logged, so a server that stays away is logged once

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
            const auto open_started = Clock::now();
            const auto opened = connection->Open(options_.url, options_.headers, kSubprotocol, options_.open_timeout_ms);
            bool up = opened.ok;
            if (!up)
                why = "the upgrade failed" + (opened.status_code != 0 ? " with HTTP " + std::to_string(opened.status_code) : std::string{}) +
                      (opened.error.empty() ? std::string{} : ": " + opened.error);
            else
                // The hello gets what the open left of the attempt's budget, and at least a second.
                up = ReadHello(*connection, (std::max<int64_t>)(options_.open_timeout_ms - ElapsedMs(open_started), 1000), why);

            if (up)
            {
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
                Log(SessionLogLevel::Info, "The collector session is up (frame limit " + std::to_string(frame_bytes) + " bytes, heartbeat " +
                                               std::to_string(heartbeat_ms / 1000) + " s).");
                reported.clear();

                const auto served_since = Clock::now();
                why = Serve(*connection);
                if (ElapsedMs(served_since) >= kStableSessionMs)
                    attempt = 0;

                int64_t linger_ms = 0;
                bool stopping = false;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    up_ = false;
                    stopping = stop_;
                    linger_ms = linger_ms_;
                    down_reason_ = stopping ? "the collector is stopping" : why;
                }
                FailAll(stopping ? "the collector is stopping" : "the session ended: " + why);
                cv_.notify_all();

                if (stopping)
                {
                    connection->Close(linger_ms);
                    return;
                }
                connection->Close(0);
                Log(SessionLogLevel::Error, "The collector session ended: " + why + ". Reconnecting.");
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
                if (why != reported)
                {
                    Log(SessionLogLevel::Error, "Cannot open a collector session: " + why + ". Values stay queued; retrying.");
                    reported = why;
                }
            }

            if (!WaitBeforeRetry(attempt++))
                return;
        }
    }
} // namespace hsm::http
