#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#include "hsm_http_transport.hpp" // HttpHeader

// The collector session as a transport (#1668, collector session step 4, ADR-0017): one WebSocket
// to GET /api/sensors/session, request frames answered by acks. Internal, NOT part of the C ABI.
//
// A collector that uses it sends nothing over the HTTP routes. There is no fallback: while no
// session is up a request fails at once, and the caller keeps its data and tries again, exactly as
// it does when the server is unreachable over HTTP.
//
// Wire (aicontext/features/api/wire-contract/feature.md, Collector session):
//   -> {"id":17,"op":"list","body":[...]}      <- {"ack":17,"status":200,"body":{...}}
//   -> {"op":"ping"}                           <- {"op":"pong"}
//   <- {"op":"hello","body":{"maxFrameBytes":N,"heartbeatSeconds":S}}   first frame of a session
namespace hsm::http
{
    class WebSocketConnection;

    struct SessionResult
    {
        // An ack arrived. False when the request never got one: no session, the session dropped,
        // the wait timed out, or the transport was cancelled.
        bool acked = false;
        // The ack's status: the HTTP status the same request would have got. 0 without an ack.
        long status = 0;
        // Why there is no ack, or the server's `error` text of a failure ack.
        std::string error;

        bool IsSuccess() const { return acked && status >= 200 && status < 300; }
    };

    enum class SessionLogLevel
    {
        Debug,
        Info,
        Error
    };

    struct SessionOptions
    {
        // ws:// or wss:// URL of the session endpoint.
        std::string url;
        // Sent with the upgrade: Key, ClientName and what the collector says about itself.
        std::vector<HttpHeader> headers;
        bool verify_peer = true;
        // Bounds one attempt to open a session: connect, TLS, upgrade and the hello frame.
        int64_t open_timeout_ms = 15000;
        // Where the transport reports what happens to the session. Called from the session thread
        // and from Start; must not throw.
        std::function<void(SessionLogLevel, const std::string&)> log;
    };

    class SessionTransport
    {
    public:
        static constexpr const char* kSubprotocol = "hsm.sensors.v1";
        // Bytes kept free in a frame for the envelope around a body.
        static constexpr size_t kEnvelopeRoom = 64;

        explicit SessionTransport(SessionOptions options);
        ~SessionTransport();

        SessionTransport(const SessionTransport&) = delete;
        SessionTransport& operator=(const SessionTransport&) = delete;

        // Starts connecting in the background and returns at once. When the libcurl of this
        // process cannot carry a WebSocket, nothing is started: one error says so, and every
        // request fails. The transport never switches to HTTP by itself.
        void Start();

        // Ends the session and the thread. With a session up, the close handshake gets up to
        // `linger_ms`. Bounded: a session thread that does not finish in the linger plus a second
        // (a write stuck on a dead peer) has its connection cancelled. Idempotent.
        void Stop(int64_t linger_ms);

        // Sends {"id":..,"op":op,"body":body} and waits up to `timeout_ms` for its ack. `body` is
        // JSON text and is embedded as it is. Thread-safe; several requests may wait at once.
        // A wait that times out ends the session (the late ack must not be taken for another
        // request's), which then reconnects.
        SessionResult Request(const char* op, const std::string& body, int64_t timeout_ms);

        // Makes waiting and later requests fail at once, until ResetCancel. The session stays up.
        void Cancel();
        void ResetCancel();

        bool IsUp() const;

        // The largest body a request may carry on the current session: the server's frame limit
        // less the envelope. 0 while no session is up.
        size_t MaxBodyBytes() const;

    private:
        struct Pending
        {
            bool done = false;
            SessionResult result;
        };

        void Run();
        void RunLoop();
        // Serves one open session until it ends. Returns why it ended, empty when stopping.
        std::string Serve(WebSocketConnection& connection);
        bool ReadHello(WebSocketConnection& connection, int64_t timeout_ms, std::string& why);
        void HandleFrame(const std::string& text);
        void FailAll(const std::string& why);
        // Waits before the next attempt; false when stopping.
        bool WaitBeforeRetry(int attempt);
        void Log(SessionLogLevel level, const std::string& message) const;

        SessionOptions options_;

        mutable std::mutex mutex_;
        std::condition_variable cv_;                                     // wakes waiting requests and the retry wait
        std::deque<std::pair<uint64_t, std::string>> outgoing_;          // frames not sent yet
        std::unordered_map<uint64_t, std::shared_ptr<Pending>> pending_; // requests waiting for an ack
        std::shared_ptr<WebSocketConnection> connection_;                // the one being opened or served
        std::string down_reason_;                                        // why there is no session, for the error of a refused request
        std::atomic<uint64_t> next_id_{ 1 };
        size_t max_frame_bytes_ = 0;
        int64_t heartbeat_ms_ = 30000;
        int64_t linger_ms_ = 0;
        bool up_ = false;
        bool stop_ = false;
        bool cancelled_ = false;
        bool reset_requested_ = false; // a request gave up waiting: the session must be reopened
        bool finished_ = true;         // the session thread is not running (or is about to return)

        std::thread thread_;
    };
} // namespace hsm::http
