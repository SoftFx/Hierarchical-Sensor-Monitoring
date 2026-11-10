#pragma once

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <random>
#include <string>
#include <vector>

#include "hsm_http_transport.hpp" // HttpHeader

// Internal WebSocket client connection for the native collector (#1662, collector session step 1,
// ADR-0017). NOT part of the C ABI and not wired into the send path yet: this is the tested
// building block the session transport is built on.
//
// libcurl provides the connection (TCP, TLS, proxy tunnelling, certificate checks) through a
// CONNECT_ONLY handle; the WebSocket handshake and framing are done here with hsm_ws_codec.hpp.
// It therefore works with any libcurl the collector supports, including distribution builds that
// lack the curl_ws_* API (see hsm_ws_codec.hpp for the reasoning). libcurl stays confined to
// hsm_ws_connection.cpp, like the HTTP transport.
namespace hsm::http
{
    struct WebSocketOpenResult
    {
        bool ok = false;
        // HTTP status of the upgrade response when one was received: 101 on success, the server's
        // status (404, 503, ...) when the upgrade was refused, 0 when no response arrived.
        long status_code = 0;
        // Subprotocol the server selected (Sec-WebSocket-Protocol of the response), empty if none.
        std::string subprotocol;
        std::string error;
    };

    enum class WebSocketReceive
    {
        Text,    // a complete message is in `text`
        Timeout, // nothing complete arrived in time; the connection is still open and usable
        Closed,  // the peer closed the WebSocket or the connection; the connection is released
        Error    // transport failure, protocol violation, cancellation or an oversized message;
                 // the connection is released
    };

    // One connection, one owner thread. Open/SendText/Receive/Close must be called from the same
    // thread; Cancel() is the only member that may be called from another one. It makes a blocked
    // Open, SendText or Receive return within one wait slice (kWaitSliceMs) and stays in force for
    // the life of the object: a cancelled connection is not reopened.
    //
    // Every terminal outcome (a failed Open, a failed SendText, Receive returning Closed or Error)
    // releases the connection, so IsOpen() is the truth and nothing is written to a finished
    // WebSocket afterwards. No member throws; Close and the destructor never block.
    //
    // Known limitation, for the session transport (step 4) to settle: waiting is a poll in
    // kWaitSliceMs slices, so an idle Receive wakes the thread ten times a second and a cancel is
    // seen within one slice. The session thread has to wake for outgoing data as well, so it needs
    // a wake-up primitive of its own; that design belongs there, not in this building block.
    class WebSocketConnection
    {
    public:
        // Messages larger than this are refused by Receive (Error) instead of growing without bound.
        static constexpr size_t kMaxMessageBytes = 8u * 1024u * 1024u;
        // The upgrade response head is refused beyond this size.
        static constexpr size_t kMaxHandshakeBytes = 16u * 1024u;
        // Longest timeout honoured; larger values are clamped (and cannot overflow the clock).
        static constexpr int64_t kMaxTimeoutMs = 24LL * 60 * 60 * 1000;
        static constexpr int64_t kWaitSliceMs = 100;
        // Longest Close() linger honoured.
        static constexpr int64_t kMaxLingerMs = 5000;
        // Interim 1xx responses tolerated in front of the 101.
        static constexpr int kMaxInterimResponses = 4;
        // The receive buffer gives its memory back once it holds less than a quarter of this while
        // its capacity exceeds it.
        static constexpr size_t kShrinkAboveBytes = 256u * 1024u;

        explicit WebSocketConnection(bool verify_peer);
        ~WebSocketConnection();

        WebSocketConnection(const WebSocketConnection&) = delete;
        WebSocketConnection& operator=(const WebSocketConnection&) = delete;

        // `url` is ws:// or wss://. `headers` are added to the upgrade request (a name or value
        // containing CR or LF is refused). `subprotocol` (may be empty) is offered in
        // Sec-WebSocket-Protocol; the server must select exactly what was offered (nothing, when
        // nothing was), otherwise the connection is closed and the result is a failure. `timeout_ms` bounds the whole open:
        // connect, TLS and the upgrade exchange.
        WebSocketOpenResult Open(
            const std::string& url,
            const std::vector<HttpHeader>& headers,
            const std::string& subprotocol,
            int64_t timeout_ms);

        // Sends one text message as one frame. False on failure, timeout or cancellation; the
        // connection is released then (a frame may be half-written).
        bool SendText(const std::string& text, int64_t timeout_ms, std::string& error);

        // Waits up to `timeout_ms` for one complete text message. Partial reads and fragmented
        // messages are assembled across calls, so a Timeout loses nothing. Pings are answered and
        // pongs ignored without surfacing. A binary message is an Error: the session is text only.
        // Answering a ping is a complete frame write, so it can extend the call by up to one
        // second beyond `timeout_ms` when the socket is not writable, and a pong that cannot be
        // written in that second ends the connection (a half-written frame would corrupt it).
        WebSocketReceive Receive(std::string& text, int64_t timeout_ms, std::string& error);

        // Sends a close frame and releases the connection. Idempotent; after Receive returned
        // Closed or Error there is nothing left to close, so no second close frame is written.
        //
        // linger_ms == 0 (the default, and what the destructor does): one non-blocking write, then
        // release. Never waits, but if inbound data is still unread the OS may reset the
        // connection and the server sees an abort instead of a normal closure.
        // linger_ms > 0: the orderly close for a planned stop. Waits up to that long (capped at
        // kMaxLingerMs) for the close frame to go out and for the peer's own close to come back,
        // dropping anything else that arrives.
        void Close(int64_t linger_ms = 0);

        void Cancel();

        bool IsOpen() const { return curl_ != nullptr; }

    private:
        enum class Io
        {
            Ok,
            Timeout,
            Closed,
            Error
        };

        enum class Wait
        {
            Ready,
            Timeout,
            Cancelled,
            Failed
        };

        using Deadline = std::chrono::steady_clock::time_point;

        static Deadline DeadlineIn(int64_t timeout_ms);

        Wait WaitForSocket(bool for_read, Deadline deadline);
        Io SendAll(const std::string& data, Deadline deadline, std::string& error);
        Io ReadMore(Deadline deadline, std::string& error);
        std::string MaskedFrame(uint8_t opcode, const std::string& payload);
        void Release();

        void* curl_ = nullptr; // CURL*, opaque so <curl/curl.h> stays in the .cpp
        bool verify_peer_;
        std::atomic<bool> cancelled_{ false };
        std::mt19937 random_;      // handshake key and frame masks; seeded once in the constructor
        std::string inbuf_;        // bytes received from the connection
        size_t inpos_ = 0;         // start of the unparsed part of inbuf_
        std::string message_;      // fragments of the message being assembled
        bool assembling_ = false;  // a non-final data frame has been received
    };
} // namespace hsm::http
