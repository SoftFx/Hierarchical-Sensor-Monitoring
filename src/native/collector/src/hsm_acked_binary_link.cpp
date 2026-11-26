#include "hsm_acked_binary_link.hpp"

#include "hsm_session_transport.hpp" // SessionTransport::kSessionIdHeader
#include "hsm_ws_connection.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>

namespace hsm::http
{
    namespace
    {
        using Clock = std::chrono::steady_clock;

        constexpr const char* kPing = "{\"op\":\"ping\"}";
        constexpr const char* kPong = "{\"op\":\"pong\"}";
        // A heartbeat frame is a few bytes: it gets this long to be written.
        constexpr int64_t kControlSendTimeoutMs = 10000;
        // The wait for the server's answer to a close the collector says after a lost ack.
        constexpr int64_t kCloseLingerMs = 1000;
        // While a test hook decides the end of an ack wait, the wait is cut into slices this long.
        constexpr int64_t kHookSliceMs = 20;
        // What one Service call reads at most: frames that keep coming must not hold the owner.
        constexpr int kServiceFrames = 64;
        constexpr int64_t kMinHeartbeatSeconds = 1;
        constexpr int64_t kMaxHeartbeatSeconds = 3600;

        int64_t RemainingMs(Clock::time_point deadline)
        {
            const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - Clock::now()).count();
            return left > 0 ? static_cast<int64_t>(left) : 0;
        }

        void PutLittleEndian(std::vector<uint8_t>& frame, size_t offset, uint64_t value, size_t bytes)
        {
            for (size_t i = 0; i < bytes; ++i)
                frame[offset + i] = static_cast<uint8_t>((value >> (8 * i)) & 0xFF);
        }

        std::string DescribeClosure(const WebSocketClosure& closure)
        {
            if (!closure.close_frame)
                return "the connection ended without a close frame";
            if (closure.code == 0)
                return "the server sent a close frame without a status code";
            return "the server sent a close frame (code " + std::to_string(closure.code) +
                   (closure.reason.empty() ? std::string(")") : ", reason \"" + AckedBinaryLink::Printable(closure.reason, 123) + "\")");
        }
    } // namespace

    std::string AckedBinaryLink::Printable(const std::string& text, size_t limit)
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

    AckedBinaryLink::AckedBinaryLink(AckedLinkOptions options)
        : options_(std::move(options))
    {
        options_.header_bytes = (std::max)(options_.header_bytes, static_cast<uint32_t>(kGenericHeaderBytes));
    }

    AckedBinaryLink::~AckedBinaryLink()
    {
        // Never waits: one non-blocking close frame, then the release (WebSocketConnection's own).
        std::shared_ptr<WebSocketConnection> connection;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            connection.swap(connection_);
        }
    }

    bool AckedBinaryLink::IsOpen() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return connection_ != nullptr && connection_->IsOpen();
    }

    void AckedBinaryLink::Cancel()
    {
        // Stored before the lock: a BeginClose that takes the lock after this one sees the flag, and
        // one that took it before has published closing_, which is cancelled here.
        cancelled_.store(true);
        std::lock_guard<std::mutex> guard(mutex_);
        if (connection_)
            connection_->Cancel();
        if (closing_)
            closing_->Cancel();
    }

    std::shared_ptr<WebSocketConnection> AckedBinaryLink::BeginClose()
    {
        std::lock_guard<std::mutex> guard(mutex_);
        std::shared_ptr<WebSocketConnection> connection;
        connection.swap(connection_);
        closing_ = connection;
        if (connection && cancelled_.load())
            connection->Cancel();
        return connection;
    }

    void AckedBinaryLink::FinishClose()
    {
        std::shared_ptr<WebSocketConnection> done;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            done.swap(closing_);
        }
    }

    void AckedBinaryLink::ResetCancel()
    {
        // A connection that was cancelled cannot be used again: it is let go.
        std::shared_ptr<WebSocketConnection> cancelled;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (cancelled_.load())
                cancelled.swap(connection_);
            cancelled_.store(false);
        }
        if (cancelled)
            cancelled->Close(0);
    }

    Clock::time_point AckedBinaryLink::NextPingDue() const
    {
        if (!IsOpen())
            return Clock::time_point::max();
        return last_sent_ + std::chrono::milliseconds(heartbeat_ms_);
    }

    void AckedBinaryLink::Ended(const std::string& why, int server_close)
    {
        std::shared_ptr<WebSocketConnection> connection;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            connection.swap(connection_);
        }
        end_ = AckedLinkEnd{};
        end_.why = why;
        end_.server_close = server_close;
        end_.close_code = server_close;
        if (connection)
            connection->Close(0);
    }

    void AckedBinaryLink::Violation(int code, const std::string& why)
    {
        // The close is said, briefly: the server learns why, and a broken server cannot hold the
        // owner thread with it. A Cancel during the linger reaches the closing connection.
        const std::shared_ptr<WebSocketConnection> connection = BeginClose();
        bool cancelled = false;
        if (connection)
        {
            if (options_.on_close_linger_for_tests)
                options_.on_close_linger_for_tests();
            connection->Close(kCloseLingerMs, code, why);
            cancelled = connection->LastCloseCancelled();
        }
        FinishClose();
        end_ = AckedLinkEnd{};
        end_.why = why;
        end_.protocol_error = true;
        end_.close_code = code;
        end_.close_cancelled = cancelled;
    }

    void AckedBinaryLink::Close(int64_t linger_ms, int code, const std::string& reason)
    {
        const std::shared_ptr<WebSocketConnection> connection = BeginClose();
        if (!connection)
        {
            FinishClose();
            return;
        }
        const bool was_open = connection->IsOpen();
        if (was_open && linger_ms > 0 && options_.on_close_linger_for_tests)
            options_.on_close_linger_for_tests();
        connection->Close(linger_ms, code, reason);
        FinishClose();
        if (was_open)
        {
            end_ = AckedLinkEnd{};
            end_.why = reason;
            end_.close_code = code;
            end_.close_cancelled = connection->LastCloseCancelled();
        }
    }

    bool AckedBinaryLink::SendPing(std::string& error)
    {
        std::shared_ptr<WebSocketConnection> connection;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            connection = connection_;
        }
        if (!connection || !connection->SendText(kPing, kControlSendTimeoutMs, error))
            return false;
        last_sent_ = Clock::now();
        return true;
    }

    AckedBinaryLink::Frame AckedBinaryLink::ReceiveOne(int64_t timeout_ms, AckedLinkAck& ack)
    {
        std::shared_ptr<WebSocketConnection> connection;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            connection = connection_;
        }
        if (!connection || !connection->IsOpen())
        {
            if (connection)
                Ended("the connection is closed", 0);
            return Frame::Ended;
        }

        std::string text;
        std::string error;
        switch (connection->Receive(text, timeout_ms, error))
        {
        case WebSocketReceive::Timeout:
            return Frame::None;
        case WebSocketReceive::Closed: {
            // Receive answered the close, echoing its code.
            const WebSocketClosure closure = connection->LastClosure();
            Ended("closed by the server: " + DescribeClosure(closure), closure.close_frame ? closure.code : 0);
            return Frame::Ended;
        }
        case WebSocketReceive::Error: {
            if (cancelled_.load())
            {
                Ended("cancelled", 0);
                return Frame::Cancelled;
            }
            const int refused = connection->LastRefusalCode();
            if (refused != 0)
            {
                // The connection said the close itself (SetCloseOnRefusal).
                Ended(refused == 1003 ? "the server sent a binary frame" : "the server sent a text frame larger than " + std::to_string(options_.max_inbound_bytes) + " bytes", 0);
                end_.protocol_error = true;
                end_.close_code = refused;
                return Frame::Ended;
            }
            Ended("the connection failed" + (error.empty() ? std::string() : ": " + Printable(error)), 0);
            return Frame::Ended;
        }
        case WebSocketReceive::Text:
            break;
        }

        const auto frame = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
        if (!frame.is_object())
        {
            Violation(1007, "the server sent a text frame that is not a JSON object");
            return Frame::Ended;
        }

        if (const auto id = frame.find("ack"); id != frame.end())
        {
            if (!id->is_number_integer())
            {
                Violation(1002, "the server sent an ack without a request id");
                return Frame::Ended;
            }
            const int64_t acked = id->get<int64_t>();
            if (acked <= 0 || acked > highest_sent_)
            {
                Violation(1002, "the server acknowledged request " + std::to_string(acked) + ", which this collector did not send");
                return Frame::Ended;
            }
            ack = AckedLinkAck{};
            ack.id = acked;
            if (const auto status = frame.find("status"); status != frame.end() && status->is_number_integer())
                ack.status = static_cast<long>((std::max<int64_t>)((std::min<int64_t>)(status->get<int64_t>(), 100000), -1));
            if (const auto words = frame.find("error"); words != frame.end() && words->is_string())
                ack.error = Printable(words->get<std::string>());
            // As the managed collector reads it (TryGetInt32): an integer that fits an int32, else
            // absent. Whether it is used, and its cap, are the caller's.
            if (const auto retry = frame.find("retryAfter"); retry != frame.end() && retry->is_number_integer())
            {
                const bool fits = retry->is_number_unsigned() ? retry->get<uint64_t>() <= static_cast<uint64_t>(INT32_MAX)
                                                              : retry->get<int64_t>() >= INT32_MIN && retry->get<int64_t>() <= INT32_MAX;
                if (fits)
                    ack.retry_after_seconds = retry->get<int64_t>();
            }
            if (const auto body = frame.find("body"); body != frame.end() && !body->is_null())
                ack.body = body->dump();
            return Frame::Ack;
        }

        const auto op = frame.find("op");
        if (op == frame.end() || !op->is_string())
        {
            Violation(1002, "the server sent a frame that is neither an ack nor a push");
            return Frame::Ended;
        }
        if (op->get<std::string>() == "ping")
        {
            if (!connection->SendText(kPong, kControlSendTimeoutMs, error))
            {
                Ended("answering a ping failed: " + Printable(error), 0);
                return Frame::Ended;
            }
            last_sent_ = Clock::now();
        }
        // A pong, or a push this version does not know: that is how version 1 grows.
        return Frame::None;
    }

    AckedLinkOpen AckedBinaryLink::Open(const std::string& session_id, const std::function<bool(const std::string& body, std::string& why)>& hello_check)
    {
        AckedLinkOpen result;
        try
        {
            Close(0, 1000, std::string());
            end_ = AckedLinkEnd{};
            if (cancelled_.load())
            {
                result.why = "cancelled";
                result.kind = "cancelled";
                return result;
            }

            auto connection = std::make_shared<WebSocketConnection>(options_.verify_peer, options_.max_inbound_bytes);
            connection->SetCloseOnRefusal(true);
            {
                std::lock_guard<std::mutex> guard(mutex_);
                connection_ = connection;
            }
            // A Cancel between the check above and the publication cancelled no connection.
            if (cancelled_.load())
                connection->Cancel();

            std::vector<HttpHeader> headers = options_.headers;
            headers.push_back({ SessionTransport::kSessionIdHeader, session_id });
            const auto started = Clock::now();
            const auto deadline = started + std::chrono::milliseconds((std::max<int64_t>)(options_.open_timeout_ms, 1));
            const auto opened = connection->Open(options_.url, headers, options_.subprotocol, options_.open_timeout_ms);
            if (!opened.ok)
            {
                Ended(std::string(), 0);
                result.status = opened.status_code != 101 ? opened.status_code : 0;
                result.why = (cancelled_.load() ? std::string("cancelled")
                                                : "the upgrade failed" + (opened.status_code != 0 ? " with HTTP " + std::to_string(opened.status_code) : std::string()) +
                                                      (opened.error.empty() ? std::string() : ": " + Printable(opened.error)));
                result.kind = cancelled_.load()           ? std::string("cancelled")
                              : opened.status_code == 101 ? std::string("upgrade:subprotocol")
                              : opened.status_code != 0   ? "upgrade:http:" + std::to_string(opened.status_code)
                                                          : "upgrade:curl:" + std::to_string(opened.curl_code);
                end_.why = result.why;
                return result;
            }

            // The hello: the first frame.
            std::string text;
            std::string error;
            const auto received = connection->Receive(text, (std::max<int64_t>)(RemainingMs(deadline), 1), error);
            if (received != WebSocketReceive::Text)
            {
                if (received == WebSocketReceive::Closed)
                    Ended("the server closed the connection before its hello: " + DescribeClosure(connection->LastClosure()), connection->LastClosure().code);
                else if (received == WebSocketReceive::Error && connection->LastRefusalCode() != 0)
                {
                    Ended("the server's first frame is not a text frame the collector takes", 0);
                    end_.protocol_error = true;
                    end_.close_code = connection->LastRefusalCode();
                }
                else if (received == WebSocketReceive::Timeout)
                {
                    Close(0, 1000, "no hello");
                    end_.why = "no hello within " + std::to_string(options_.open_timeout_ms) + " ms";
                }
                else
                    Ended(cancelled_.load() ? std::string("cancelled") : "no hello: " + Printable(error), 0);
                result.why = end_.why;
                result.protocol_error = end_.protocol_error;
                result.kind = cancelled_.load()                       ? std::string("cancelled")
                              : received == WebSocketReceive::Closed  ? "hello:closed:" + std::to_string(end_.server_close)
                              : received == WebSocketReceive::Timeout ? std::string("hello:timeout")
                              : end_.protocol_error                   ? "hello:protocol:" + std::to_string(end_.close_code)
                                                                      : std::string("hello:failed");
                return result;
            }

            const auto hello = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
            const auto op = hello.is_object() ? hello.find("op") : hello.end();
            if (!hello.is_object() || op == hello.end() || !op->is_string() || op->get<std::string>() != "hello" || !hello.contains("body") ||
                !hello["body"].is_object())
            {
                Violation(1002, "the first frame of the connection is not a hello");
                result.why = end_.why;
                result.protocol_error = true;
                result.kind = "hello:protocol:not-a-hello";
                return result;
            }
            const auto& body = hello["body"];
            const auto heartbeat = body.find("heartbeatSeconds");
            const int64_t heartbeat_seconds = heartbeat != body.end() && heartbeat->is_number_integer() ? heartbeat->get<int64_t>() : 0;
            if (heartbeat_seconds < kMinHeartbeatSeconds || heartbeat_seconds > kMaxHeartbeatSeconds)
            {
                Violation(1002, "the hello states a heartbeat this collector cannot work with (heartbeatSeconds " + std::to_string(heartbeat_seconds) + ")");
                result.why = end_.why;
                result.protocol_error = true;
                result.kind = "hello:protocol:heartbeat";
                return result;
            }
            result.hello_body = body.dump();
            std::string refused;
            if (hello_check && !hello_check(result.hello_body, refused))
            {
                Violation(1002, refused);
                result.why = end_.why;
                result.protocol_error = true;
                result.kind = "hello:protocol:limits";
                return result;
            }
            heartbeat_ms_ = heartbeat_seconds * 1000;
            result.heartbeat_ms = heartbeat_ms_;

            // The probe: a header without a request, answered 200 as the first answer.
            highest_sent_ = 0;
            std::vector<uint8_t> probe(options_.header_bytes, 0);
            const int64_t id = next_id_++;
            probe[0] = kFrameVersion;
            probe[1] = options_.probe_kind;
            PutLittleEndian(probe, 4, options_.header_bytes, 4);
            PutLittleEndian(probe, 8, static_cast<uint64_t>(id), 8);
            highest_sent_ = id;
            if (!connection->SendBinary(reinterpret_cast<const char*>(probe.data()), probe.size(), (std::max<int64_t>)(RemainingMs(deadline), 1), error))
            {
                Ended(cancelled_.load() ? std::string("cancelled") : "sending the probe failed: " + Printable(error), 0);
                result.why = end_.why;
                result.kind = cancelled_.load() ? "cancelled" : "probe:send";
                return result;
            }
            last_sent_ = Clock::now();

            for (;;)
            {
                const int64_t left = RemainingMs(deadline);
                if (left <= 0)
                {
                    Close(0, 1000, "no probe answer");
                    end_.why = "no hello and probe answer within " + std::to_string(options_.open_timeout_ms) + " ms";
                    result.why = end_.why;
                    result.kind = "probe:timeout";
                    return result;
                }
                AckedLinkAck ack;
                const Frame frame = ReceiveOne(left, ack);
                if (frame == Frame::None)
                    continue;
                if (frame != Frame::Ack)
                {
                    result.why = end_.why;
                    result.protocol_error = end_.protocol_error;
                    result.kind = frame == Frame::Cancelled ? std::string("cancelled")
                                  : end_.protocol_error     ? "probe:protocol:" + std::to_string(end_.close_code)
                                                            : "probe:ended:" + std::to_string(end_.server_close);
                    return result;
                }
                if (ack.id != id)
                {
                    Violation(1002, "the server's first answer is not the ack of the probe");
                    result.why = end_.why;
                    result.protocol_error = true;
                    result.kind = "probe:protocol:not-the-probe";
                    return result;
                }
                if (ack.status != 200)
                {
                    Violation(1002, "the server answered the probe with status " + std::to_string(ack.status));
                    result.why = end_.why;
                    result.protocol_error = true;
                    result.kind = "probe:status:" + std::to_string(ack.status);
                    return result;
                }
                break;
            }

            result.ok = true;
            return result;
        }
        catch (...)
        {
            Ended("the open failed", 0);
            result.ok = false;
            try
            {
                result.why = "the open failed";
                result.kind = "failed";
            }
            catch (...)
            {
            }
            return result;
        }
    }

    AckedLinkPutResult AckedBinaryLink::Put(uint8_t kind, std::vector<uint8_t>& frame, int64_t send_timeout_ms, int64_t ack_timeout_ms, AckedLinkAck& ack)
    {
        try
        {
            if (cancelled_.load())
                return AckedLinkPutResult::Cancelled;
            std::shared_ptr<WebSocketConnection> connection;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                connection = connection_;
            }
            if (!connection || !connection->IsOpen() || frame.size() < options_.header_bytes)
                return AckedLinkPutResult::NotOpen;

            const int64_t id = next_id_++;
            frame[0] = kFrameVersion;
            frame[1] = kind;
            frame[2] = 0;
            frame[3] = 0;
            PutLittleEndian(frame, 4, options_.header_bytes, 4);
            PutLittleEndian(frame, 8, static_cast<uint64_t>(id), 8);
            highest_sent_ = id;

            std::string error;
            if (!connection->SendBinary(reinterpret_cast<const char*>(frame.data()), frame.size(), send_timeout_ms, error))
                return SendFailed(error);
            last_sent_ = Clock::now();
            return AwaitAck(id, ack_timeout_ms, ack);
        }
        catch (...)
        {
            Ended("the request failed", 0);
            return AckedLinkPutResult::Lost;
        }
    }

    AckedLinkPutResult AckedBinaryLink::Request(const std::string& op, const std::string& body_json, int64_t send_timeout_ms, int64_t ack_timeout_ms, AckedLinkAck& ack)
    {
        try
        {
            if (cancelled_.load())
                return AckedLinkPutResult::Cancelled;
            std::shared_ptr<WebSocketConnection> connection;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                connection = connection_;
            }
            if (!connection || !connection->IsOpen())
                return AckedLinkPutResult::NotOpen;

            const int64_t id = next_id_++;
            // The envelope is built by the JSON library; the body is the caller's JSON text, parsed
            // first so that the frame is one JSON object whatever the caller passed.
            nlohmann::json request = nlohmann::json::object();
            request["id"] = id;
            request["op"] = op;
            request["body"] = body_json.empty() ? nlohmann::json::object() : nlohmann::json::parse(body_json);
            const std::string text = request.dump();
            highest_sent_ = id;

            std::string error;
            if (!connection->SendText(text, send_timeout_ms, error))
                return SendFailed(error);
            last_sent_ = Clock::now();
            return AwaitAck(id, ack_timeout_ms, ack);
        }
        catch (...)
        {
            Ended("the request failed", 0);
            return AckedLinkPutResult::Lost;
        }
    }

    AckedLinkPutResult AckedBinaryLink::SendFailed(const std::string& error)
    {
        if (cancelled_.load())
        {
            Ended("cancelled", 0);
            return AckedLinkPutResult::Cancelled;
        }
        Ended("writing a request failed: " + Printable(error), 0);
        return AckedLinkPutResult::Lost;
    }

    // The wait for the ack of request `id`, which was written whole: the deadline starts now.
    AckedLinkPutResult AckedBinaryLink::AwaitAck(int64_t id, int64_t ack_timeout_ms, AckedLinkAck& ack)
    {
        std::string error;
        const auto deadline = Clock::now() + std::chrono::milliseconds((std::max<int64_t>)(ack_timeout_ms, 0));
        for (;;)
        {
            const bool hooked = static_cast<bool>(options_.ack_abandoned_for_tests);
            const bool expired = hooked ? options_.ack_abandoned_for_tests(id) : RemainingMs(deadline) <= 0;
            if (expired)
            {
                // No ack in time: the connection is closed, so a late ack cannot be taken for the
                // next request's; the caller sends again on the next connection.
                Close(kCloseLingerMs, 1000, "no ack in time");
                end_.why = "a request got no ack within " + std::to_string(ack_timeout_ms) + " ms";
                return AckedLinkPutResult::Lost;
            }
            // Nothing was sent for a heartbeat while the answer is awaited: a ping keeps the
            // server's silence limit away from a long store.
            if (Clock::now() - last_sent_ >= std::chrono::milliseconds(heartbeat_ms_) && !SendPing(error))
            {
                if (cancelled_.load())
                {
                    Ended("cancelled", 0);
                    return AckedLinkPutResult::Cancelled;
                }
                Ended("sending a heartbeat failed: " + Printable(error), 0);
                return AckedLinkPutResult::Lost;
            }
            const int64_t until_ping = (std::max<int64_t>)(heartbeat_ms_ - std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now() - last_sent_).count(), 1);
            const int64_t slice = hooked ? kHookSliceMs : (std::max<int64_t>)((std::min)(RemainingMs(deadline), until_ping), 1);
            const Frame got = ReceiveOne(slice, ack);
            switch (got)
            {
            case Frame::Ack:
                if (ack.id == id)
                    return AckedLinkPutResult::Acked;
                // A late ack of an earlier request of this connection: nobody waits for it.
                break;
            case Frame::None:
                break;
            case Frame::Ended:
                return AckedLinkPutResult::Lost;
            case Frame::Cancelled:
                return AckedLinkPutResult::Cancelled;
            }
        }
    }

    bool AckedBinaryLink::Service()
    {
        try
        {
            if (cancelled_.load() || !IsOpen())
                return false;
            for (int i = 0; i < kServiceFrames; ++i)
            {
                AckedLinkAck ack;
                const Frame got = ReceiveOne(0, ack);
                if (got == Frame::None)
                    break;
                if (got == Frame::Ended || got == Frame::Cancelled)
                    return false;
                // An ack outside a request: a late one of this connection, dropped.
            }
            if (Clock::now() - last_sent_ >= std::chrono::milliseconds(heartbeat_ms_))
            {
                std::string error;
                if (!SendPing(error))
                {
                    Ended("sending a heartbeat failed: " + Printable(error), 0);
                    return false;
                }
            }
            return IsOpen();
        }
        catch (...)
        {
            Ended("servicing the connection failed", 0);
            return false;
        }
    }
} // namespace hsm::http
