#pragma once

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

#include "hsm_http_transport.hpp" // HttpHeader

// Internal WebSocket client connection for the native collector (#1662, collector session step 1,
// ADR-0017). NOT part of the C ABI and not wired into the send path yet: this is the tested
// building block the session transport is built on.
//
// The WebSocket protocol itself is libcurl's: the upgrade exchange, masking, frame encoding and
// frame parsing all happen inside libcurl (CONNECT_ONLY=2 with curl_ws_send / curl_ws_recv). This
// class adds no protocol code of its own. What it adds is policy around libcurl's API: timeouts,
// cancellation, whole-message assembly with a size limit, text only, and the subprotocol check.
// libcurl stays confined to hsm_ws_connection.cpp, like the HTTP transport.
//
// The price of not owning the protocol is that a session exists only where libcurl can carry one:
// see Supported(). Elsewhere the collector keeps the HTTP transport.
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
        // The address libcurl connected to, "ip:port" ("[ip]:port" for IPv6); empty when no
        // connection was made. With a CDN or a proxy in front it is theirs, not the server's.
        std::string peer;
    };

    // How the peer ended the connection: filled when Receive returns Closed, cleared by Open.
    struct WebSocketClosure
    {
        // True: a close frame arrived. False: the TCP connection ended without one.
        bool close_frame = false;
        // The close frame's status code; 0 when the frame carried none.
        int code = 0;
        // The close frame's reason text (at most 123 bytes), as the peer sent it.
        std::string reason;
    };

    enum class WebSocketReceive
    {
        Text,    // a complete message is in `text`
        Timeout, // nothing complete arrived in time; the connection is still open and usable
        Closed,  // the peer closed the WebSocket or the connection (LastClosure says which); the
                 // connection is released
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
    // WebSocket afterwards. No member throws; Close(0) and the destructor never block.
    //
    // Known limitation, for the session transport (step 4) to settle: waiting is a poll in
    // kWaitSliceMs slices, so an idle Receive wakes the thread ten times a second and a cancel is
    // seen within one slice. The session thread has to wake for outgoing data as well, so it needs
    // a wake-up primitive of its own; that design belongs there, not in this building block.
    class WebSocketConnection
    {
    public:
        // Messages larger than this are refused by Receive (Error) instead of growing without
        // bound. The size comes from the frame header, so the refusal happens before the payload
        // is read.
        static constexpr size_t kMaxMessageBytes = 8u * 1024u * 1024u;
        // Longest timeout honoured; larger values are clamped (and cannot overflow the clock).
        static constexpr int64_t kMaxTimeoutMs = 24LL * 60 * 60 * 1000;
        static constexpr int64_t kWaitSliceMs = 100;
        // Longest Close() linger honoured.
        static constexpr int64_t kMaxLingerMs = 5000;
        // Oldest libcurl accepted, as LIBCURL_VERSION_NUM: 8.14.0. Two things this class relies on
        // are missing before it. Up to 8.12 the frames of a fragmented message are reported
        // without saying which one is the last (the flags came from the opcode alone), so a
        // message cannot be assembled reliably. And 8.14 is the first release that lets the caller
        // answer pings (CURLWS_NOAUTOPONG): libcurl's automatic pong is not something to build on,
        // as 8.21 keeps it queued until the next send or the next data frame.
        static constexpr unsigned int kMinCurlVersion = 0x080e00;

        // True when the libcurl loaded into this process can carry a WebSocket: it is at least
        // kMinCurlVersion and was built with the ws/wss protocols (distribution builds older than
        // 8.11 leave them out). When false, `reason` (if given) says why and Open fails with the
        // same text; the caller stays on HTTP.
        static bool Supported(std::string* reason = nullptr) noexcept;

        explicit WebSocketConnection(bool verify_peer);
        ~WebSocketConnection();

        WebSocketConnection(const WebSocketConnection&) = delete;
        WebSocketConnection& operator=(const WebSocketConnection&) = delete;

        // `url` is ws:// or wss://. `headers` are added to the upgrade request. A name that is not
        // a plain header token, a value containing CR or LF, and a name that would replace one of
        // the upgrade's own headers (Upgrade, Connection, Sec-WebSocket-*) are refused. `subprotocol` (may be empty) is offered in
        // Sec-WebSocket-Protocol; the server must select exactly what was offered (nothing, when
        // nothing was), otherwise the connection is closed and the result is a failure.
        // `timeout_ms` bounds the whole open: connect, TLS and the upgrade exchange.
        WebSocketOpenResult Open(
            const std::string& url,
            const std::vector<HttpHeader>& headers,
            const std::string& subprotocol,
            int64_t timeout_ms);

        // Sends one text message as one frame. False on failure, timeout or cancellation; the
        // connection is released then (a frame may be half-written).
        bool SendText(const std::string& text, int64_t timeout_ms, std::string& error);

        // Waits up to `timeout_ms` for one complete text message. Partial reads and fragmented
        // messages are assembled across calls, so a Timeout loses nothing; and it is a Timeout
        // whenever no message is complete by then, even if frames kept arriving. Pings are answered and
        // pongs dropped without surfacing. A binary message is an Error: the session is text only.
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

        // How the last Receive that returned Closed saw the connection end.
        const WebSocketClosure& LastClosure() const { return closure_; }

    private:
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
        bool SendFrame(const char* data, size_t size, unsigned int flags, Deadline deadline, std::string& error);
        std::string Describe(int curl_code) const;
        void ForgetDetail();
        void Release();

        void* curl_ = nullptr;    // CURL*, opaque so <curl/curl.h> stays in the .cpp
        void* headers_ = nullptr; // curl_slist* of the upgrade request, owned for the handle's life
        bool verify_peer_;
        std::atomic<bool> cancelled_{ false };
        std::string message_;      // payload received so far of the message being assembled
        std::string control_;      // payload received so far of the ping or close frame in progress
        std::vector<char> detail_; // libcurl's error text for the handle (CURLOPT_ERRORBUFFER)
        WebSocketClosure closure_; // how the peer ended the connection, kept past Release
    };
} // namespace hsm::http
