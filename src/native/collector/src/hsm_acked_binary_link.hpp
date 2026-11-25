#pragma once

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include "hsm_http_transport.hpp" // HttpHeader

// A second WebSocket tied to the collector session that carries binary requests, each answered by
// one text ack (#1848; wire contract, "Table connection"). Internal, NOT part of the C ABI. Generic:
// the table connection is its first user, the large file values of epic #1882 are meant to be the
// next.
//
// What is generic and lives here: the upgrade to the session route with the caller's subprotocol and
// X-Hsm-Session, the hello envelope ({"op":"hello","body":{...}}) and its heartbeatSeconds, the probe,
// bytes 0..15 of the frame header (version 1, kind, reserved 0, header length, request id), the ack
// envelope ({"ack":id,"status":N,"error":"...","retryAfter":S,"body":{...}}), the heartbeat (ping and
// pong) and the protocol errors of the server's frames. What belongs to the caller: the subprotocol,
// the rest of the frame header, what the hello's body means, what an ack's body means.
//
// The WebSocket is libcurl's (WebSocketConnection) and the JSON is nlohmann's: no protocol code of its
// own. One owner thread calls everything but Cancel, which any thread may call. Nothing throws.
namespace hsm::http
{
    class WebSocketConnection;

    struct AckedLinkOptions
    {
        // ws:// or wss:// URL of the session route.
        std::string url;
        // The session's upgrade headers (Key, ClientName, ...); X-Hsm-Session is added per open.
        std::vector<HttpHeader> headers;
        bool verify_peer = true;
        std::string subprotocol;
        // Bytes of the frame header, at least 16: the probe is a header of this length, zero past
        // byte 15, and the length is written into bytes 4..7 of every frame.
        uint32_t header_bytes = 40;
        // The kind byte of the probe.
        uint8_t probe_kind = 2;
        // Bounds one open: the upgrade, the hello and the probe's ack.
        int64_t open_timeout_ms = 15000;
        // The largest text message taken from the server; a larger one is a protocol error (1009).
        size_t max_inbound_bytes = 1024 * 1024;
        // For tests: polled while a request waits for its ack; true ends the wait as "no ack in time"
        // in place of the request timeout. Called on the owner thread with no lock held.
        std::function<bool(int64_t request_id)> ack_abandoned_for_tests;
        // For tests: called on the owner thread when a close with a linger has taken its connection
        // out of use and is about to wait for the peer's answer (Cancel reaches the closing
        // connection from then on).
        std::function<void()> on_close_linger_for_tests;
    };

    // The answer to one request.
    struct AckedLinkAck
    {
        int64_t id = 0;
        long status = 0;
        // The ack's `error` text (empty without one).
        std::string error;
        // The ack's `retryAfter` in seconds; -1 when it carried none, or not an integer that fits an
        // int32 (the managed collector's TryGetInt32: 1.5 is absent).
        int64_t retry_after_seconds = -1;
        // The ack's `body` as JSON text; empty when it carried none.
        std::string body;
    };

    // How the last connection ended. Filled when IsOpen() turns false.
    struct AckedLinkEnd
    {
        std::string why;
        // The close code the server sent; 0 when it sent no close frame (or the collector ended it).
        int server_close = 0;
        // The server sent something this client does not take; the connection was closed with
        // `close_code` and the caller reports it once.
        bool protocol_error = false;
        // The code the collector closed with (its own close or its answer to the server's).
        int close_code = 0;
        // The collector's close with a linger ended because Cancel reached the closing connection.
        bool close_cancelled = false;
    };

    struct AckedLinkOpen
    {
        bool ok = false;
        // The HTTP status of a refused upgrade; 0 for every other failure.
        long status = 0;
        std::string why;
        // The hello's body as JSON text, for the caller to read its own fields.
        std::string hello_body;
        int64_t heartbeat_ms = 0;
        // The hello, the probe's answer or another frame broke the protocol (the connection was
        // closed with a code of its own).
        bool protocol_error = false;
        // What kind of failure it was, without its words: the step (upgrade, hello, probe) and the
        // HTTP status, libcurl's result code or the close code. `why` may carry elapsed times
        // (libcurl's text); this does not, so a failure that repeats can be said once.
        std::string kind;
    };

    enum class AckedLinkPutResult
    {
        Acked,     // `ack` holds the answer
        Lost,      // the connection ended before an ack (LastEnd says how); the request may have arrived
        Cancelled, // Cancel was called; the connection is released
        NotOpen,   // nothing was sent: no connection is open
    };

    class AckedBinaryLink
    {
    public:
        static constexpr uint8_t kFrameVersion = 1;
        static constexpr size_t kGenericHeaderBytes = 16;

        explicit AckedBinaryLink(AckedLinkOptions options);
        ~AckedBinaryLink();

        AckedBinaryLink(const AckedBinaryLink&) = delete;
        AckedBinaryLink& operator=(const AckedBinaryLink&) = delete;

        // Opens a connection named after the session `session_id` (X-Hsm-Session): the upgrade, the
        // hello (its heartbeatSeconds within 1 s .. 1 h), then the probe and its ack, 200, as the first
        // answer. A connection still open is closed first. `hello_check` (may be empty) reads the
        // hello's body; false, with `why`, refuses it: the connection is closed with 1002.
        AckedLinkOpen Open(const std::string& session_id, const std::function<bool(const std::string& body, std::string& why)>& hello_check);

        // Sends one request: writes bytes 0..15 of `frame`'s header (the caller filled the rest and
        // reserved the bytes), one binary message, then waits up to `ack_timeout_ms` for the ack of
        // its request id. `send_timeout_ms` bounds the write. A connection over which no ack arrived
        // in time is closed with 1000, so that a late ack cannot be taken for another request's.
        AckedLinkPutResult Put(uint8_t kind, std::vector<uint8_t>& frame, int64_t send_timeout_ms, int64_t ack_timeout_ms, AckedLinkAck& ack);

        // Between requests: reads what the server sent meanwhile (pings are answered, pongs dropped,
        // a close or a protocol error ends the connection) and sends a ping when nothing was sent for
        // a heartbeat. Never waits for the network beyond a ping's write. False when no connection is
        // open (any more).
        bool Service();

        // When Service needs to run next to keep the heartbeat; max() when nothing is open.
        std::chrono::steady_clock::time_point NextPingDue() const;

        // Closes the open connection with a close frame (code, reason), waiting up to `linger_ms`
        // for the server's own close; 0 writes the frame once without waiting. Idempotent.
        void Close(int64_t linger_ms, int code, const std::string& reason);

        // Ends what is in flight at once and refuses every later Open and Put, until ResetCancel.
        // Any thread.
        void Cancel();
        void ResetCancel();
        bool Cancelled() const { return cancelled_.load(); }

        bool IsOpen() const;
        const AckedLinkEnd& LastEnd() const { return end_; }
        int64_t HeartbeatMs() const { return heartbeat_ms_; }

        // Ack text of a broken server is cut to this before it reaches a log line.
        static std::string Printable(const std::string& text, size_t limit = 300);

    private:
        enum class Frame
        {
            None,     // nothing complete arrived
            Ack,      // `ack` holds one
            Ended,    // the connection is over (end_ says how)
            Cancelled // Cancel was called
        };

        // One receive of up to `timeout_ms`: pings answered, pongs and unknown pushes dropped.
        Frame ReceiveOne(int64_t timeout_ms, AckedLinkAck& ack);
        // Closes with `code` after a protocol error of the server.
        void Violation(int code, const std::string& why);
        // Takes the open connection out of use for a close: it stays reachable by Cancel (closing_)
        // until FinishClose, and is cancelled at once when Cancel came first.
        std::shared_ptr<WebSocketConnection> BeginClose();
        void FinishClose();
        // The connection ended without the collector's word (a close, an error): released.
        void Ended(const std::string& why, int server_close);
        bool SendPing(std::string& error);

        AckedLinkOptions options_;
        std::atomic<bool> cancelled_{ false };
        // Guards connection_ against Cancel from another thread; the owner thread uses the object
        // itself without it (Cancel only flips an atomic inside it).
        mutable std::mutex mutex_;
        std::shared_ptr<WebSocketConnection> connection_;
        // The connection being closed with a linger: out of use, still reachable by Cancel.
        std::shared_ptr<WebSocketConnection> closing_;
        AckedLinkEnd end_;
        int64_t heartbeat_ms_ = 30000;
        int64_t next_id_ = 1;
        int64_t highest_sent_ = 0; // of the open connection
        std::chrono::steady_clock::time_point last_sent_{};
    };
} // namespace hsm::http
