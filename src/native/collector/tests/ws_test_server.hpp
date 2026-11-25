#pragma once

// Minimal in-process WebSocket server for the native WebSocket tests (#1662). Test-only; compiled
// only in the HSM_COLLECTOR_HTTP build. Implements just enough of RFC 6455 to exercise a client:
// the upgrade handshake, masked client frames, unmasked server frames with 7/16/64-bit lengths,
// ping/pong and the close handshake. Plaintext only, like the HTTP capture servers next to it.
// Serves one connection at a time on its own thread, unless Options::side_subprotocol is set: then
// an upgrade that offers that subprotocol is a SIDE connection (the table connection of #1848),
// served on a thread of its own while the main connection goes on, so that both are open at once.
// Text and binary messages from the client are recorded for the test, whole (fragments joined).
//
// This is the only RFC 6455 code the repository owns, and it never ships: the collector's client
// side is libcurl's. Being a second, independent implementation is the point of it. Its handshake
// digest is pinned by the RFC's own example in native_ws_fixture_accept_key_matches_rfc.

#include "http_capture_server.hpp" // socket_t, platform socket headers

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace hsm::test
{
    namespace ws_detail
    {
        inline std::string HeaderValue(const std::string& head, const std::string& name_lower)
        {
            std::string lower = head;
            for (auto& c : lower)
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

            const size_t pos = lower.find("\r\n" + name_lower + ":");
            if (pos == std::string::npos)
                return {};
            const size_t start = pos + 2 + name_lower.size() + 1;
            const size_t end = head.find("\r\n", start);
            std::string value = head.substr(start, end == std::string::npos ? std::string::npos : end - start);
            const size_t first = value.find_first_not_of(" \t");
            const size_t last = value.find_last_not_of(" \t");
            return first == std::string::npos ? std::string{} : value.substr(first, last - first + 1);
        }

        inline uint32_t RotateLeft(uint32_t value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        // SHA-1 (FIPS 180-1), for the Sec-WebSocket-Accept value a server owes the client. The protocol
        // fixes it to SHA-1; it protects nothing secret.
        inline std::array<uint8_t, 20> Sha1(std::string_view input)
        {
            uint32_t h0 = 0x67452301u, h1 = 0xEFCDAB89u, h2 = 0x98BADCFEu, h3 = 0x10325476u, h4 = 0xC3D2E1F0u;

            std::string message(input);
            const uint64_t bit_length = static_cast<uint64_t>(input.size()) * 8u;
            message.push_back(static_cast<char>(0x80));
            while (message.size() % 64 != 56)
                message.push_back('\0');
            for (int shift = 56; shift >= 0; shift -= 8)
                message.push_back(static_cast<char>((bit_length >> shift) & 0xFFu));

            for (size_t chunk = 0; chunk < message.size(); chunk += 64)
            {
                uint32_t w[80];
                for (size_t i = 0; i < 16; ++i)
                {
                    const size_t at = chunk + i * 4;
                    w[i] = (static_cast<uint32_t>(static_cast<uint8_t>(message[at])) << 24) |
                           (static_cast<uint32_t>(static_cast<uint8_t>(message[at + 1])) << 16) |
                           (static_cast<uint32_t>(static_cast<uint8_t>(message[at + 2])) << 8) |
                           static_cast<uint32_t>(static_cast<uint8_t>(message[at + 3]));
                }
                for (size_t i = 16; i < 80; ++i)
                    w[i] = RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

                uint32_t a = h0, b = h1, c = h2, d = h3, e = h4;
                for (size_t i = 0; i < 80; ++i)
                {
                    uint32_t f = 0, k = 0;
                    if (i < 20)
                    {
                        f = (b & c) | (~b & d);
                        k = 0x5A827999u;
                    }
                    else if (i < 40)
                    {
                        f = b ^ c ^ d;
                        k = 0x6ED9EBA1u;
                    }
                    else if (i < 60)
                    {
                        f = (b & c) | (b & d) | (c & d);
                        k = 0x8F1BBCDCu;
                    }
                    else
                    {
                        f = b ^ c ^ d;
                        k = 0xCA62C1D6u;
                    }
                    const uint32_t temp = RotateLeft(a, 5) + f + e + k + w[i];
                    e = d;
                    d = c;
                    c = RotateLeft(b, 30);
                    b = a;
                    a = temp;
                }
                h0 += a;
                h1 += b;
                h2 += c;
                h3 += d;
                h4 += e;
            }

            std::array<uint8_t, 20> digest{};
            const uint32_t words[5] = { h0, h1, h2, h3, h4 };
            for (size_t i = 0; i < 5; ++i)
            {
                digest[i * 4] = static_cast<uint8_t>((words[i] >> 24) & 0xFFu);
                digest[i * 4 + 1] = static_cast<uint8_t>((words[i] >> 16) & 0xFFu);
                digest[i * 4 + 2] = static_cast<uint8_t>((words[i] >> 8) & 0xFFu);
                digest[i * 4 + 3] = static_cast<uint8_t>(words[i] & 0xFFu);
            }
            return digest;
        }

        // FNV-1a, 64 bits: a checksum for the tests to compare a large binary message with what was
        // sent. Not a protection against anything.
        inline uint64_t Fnv1a64(std::string_view bytes)
        {
            uint64_t hash = 0xcbf29ce484222325ull;
            for (const char c : bytes)
            {
                hash ^= static_cast<uint8_t>(c);
                hash *= 0x100000001b3ull;
            }
            return hash;
        }

        inline std::string Hex64(uint64_t value)
        {
            static const char digits[] = "0123456789abcdef";
            std::string out(16, '0');
            for (int i = 15; i >= 0; --i, value >>= 4)
                out[static_cast<size_t>(i)] = digits[value & 0xFu];
            return out;
        }

        // True when the comma-separated Sec-WebSocket-Protocol offer contains `subprotocol`.
        inline bool Offers(const std::string& offer, const std::string& subprotocol)
        {
            size_t start = 0;
            while (start <= offer.size())
            {
                const size_t end = (std::min)(offer.find(',', start), offer.size());
                std::string token = offer.substr(start, end - start);
                const size_t first = token.find_first_not_of(" \t");
                const size_t last = token.find_last_not_of(" \t");
                if (first != std::string::npos && token.substr(first, last - first + 1) == subprotocol)
                    return true;
                start = end + 1;
            }
            return false;
        }

        inline std::string Base64(const uint8_t* data, size_t size)
        {
            static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            std::string out;
            out.reserve((size + 2) / 3 * 4);
            for (size_t i = 0; i < size; i += 3)
            {
                const uint32_t b0 = data[i];
                const uint32_t b1 = i + 1 < size ? data[i + 1] : 0u;
                const uint32_t b2 = i + 2 < size ? data[i + 2] : 0u;
                const uint32_t triple = (b0 << 16) | (b1 << 8) | b2;
                out.push_back(alphabet[(triple >> 18) & 0x3Fu]);
                out.push_back(alphabet[(triple >> 12) & 0x3Fu]);
                out.push_back(i + 1 < size ? alphabet[(triple >> 6) & 0x3Fu] : '=');
                out.push_back(i + 2 < size ? alphabet[triple & 0x3Fu] : '=');
            }
            return out;
        }

        // The value a server must return in Sec-WebSocket-Accept for a given Sec-WebSocket-Key.
        inline std::string AcceptKey(std::string_view client_key)
        {
            std::string material(client_key);
            material += "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            const auto digest = Sha1(material);
            return Base64(digest.data(), digest.size());
        }
    } // namespace ws_detail

    // What the file side does with one message of a file connection (#1887).
    struct FileSideAction
    {
        enum class Kind
        {
            None,  // nothing is sent
            Reply, // `text` is sent as a text message
            Close, // the connection is closed with `code` and `reason`, then read on until the client's close
            Drop,  // the TCP connection is cut without a close frame
        };
        Kind kind = Kind::None;
        std::string text;
        int code = 0;
        std::string reason;

        static FileSideAction Nothing() { return {}; }
        static FileSideAction ReplyWith(std::string text) { return { Kind::Reply, std::move(text), 0, {} }; }
        static FileSideAction CloseWith(int code, std::string reason) { return { Kind::Close, {}, code, std::move(reason) }; }
        static FileSideAction Cut() { return { Kind::Drop, {}, 0, {} }; }
    };

    // The server side of hsm.files.v1 that WebSocketTestServer serves file connections with
    // (EnableFileScript). tests/file_server_script.hpp is the one the tests use; this interface keeps the
    // server free of zstd, zlib and SHA-256. Called from the connection threads, at once: an
    // implementation guards itself. Nothing it throws is let out of a server thread.
    class FileSideHandler
    {
    public:
        virtual ~FileSideHandler() = default;
        // The text sent first on every accepted file connection.
        virtual std::string HelloText() = 0;
        // A file connection was accepted; `connection` counts the side connections from 1.
        virtual void Opened(int connection, const std::string& session_id) = 0;
        virtual FileSideAction OnText(int connection, const std::string& text) = 0;
        virtual FileSideAction OnBinary(int connection, const std::string& frame) = 0;
        // The connection is over, or replaced: the upload it holds is given back. Called once or more.
        virtual void Released(int connection) = 0;
    };

    class WebSocketTestServer
    {
    public:
        struct Options
        {
            // Answer the upgrade request with this status instead of upgrading (0 = upgrade).
            int refuse_with_status = 0;
            // Subprotocol to select in the response; empty = select none.
            std::string subprotocol;
            // Answer every text message with "echo:" + the message.
            bool echo = true;
            // Send this interim response (e.g. 103) before the final one; 0 = none.
            int interim_status = 0;
            // Behave like the collector session endpoint of an HSM server: select the session
            // subprotocol, send a hello frame after the upgrade, answer every request frame with
            // an ack and every application ping with a pong. `echo` is ignored in this mode.
            bool session = false;
            int max_frame_bytes = 4 * 1024 * 1024;
            int heartbeat_seconds = 30;
            // The subprotocol of side connections (e.g. "hsm.tables.v1"). Empty: none, and one
            // connection at a time served on the accept thread, exactly as before. Set: an upgrade
            // that offers it is answered by the side script (RefuseSideUpgradesWith, SetSideHello)
            // and served on a thread of its own; every other connection is a main connection,
            // served one at a time on a thread of its own, so both can be open at once.
            std::string side_subprotocol;
        };

        WebSocketTestServer()
            : WebSocketTestServer(Options{}) {}

        explicit WebSocketTestServer(Options options)
            : options_(std::move(options))
        {
            refuse_status_.store(options_.refuse_with_status, std::memory_order_release);
            if (options_.session && options_.subprotocol.empty())
                options_.subprotocol = "hsm.sensors.v1";
#if defined(_WIN32)
            WSADATA wsa;
            WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
            listen_ = socket(AF_INET, SOCK_STREAM, 0);
            if (listen_ == INVALID_SOCKET)
                return; // Port() stays 0: the test fails on the first connect

            sockaddr_in addr{};
            addr.sin_family = AF_INET;
            addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
            addr.sin_port = 0; // ephemeral

            int yes = 1;
            setsockopt(listen_, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&yes), sizeof(yes));
            if (bind(listen_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0 || listen(listen_, 4) != 0)
            {
                closesocket(listen_);
                listen_ = INVALID_SOCKET;
                return;
            }

            sockaddr_in bound{};
            socklen_t len = sizeof(bound);
            getsockname(listen_, reinterpret_cast<sockaddr*>(&bound), &len);
            port_ = ntohs(bound.sin_port);

            worker_ = std::thread([this] { AcceptLoop(); });
        }

        ~WebSocketTestServer()
        {
            stop_.store(true, std::memory_order_release);
            // A push ends once its frame is written or the client has gone.
            JoinPushes();
            if (worker_.joinable())
                worker_.join();
            // Every connection thread sees stop_ within one 20 ms poll and closes its socket.
            if (main_thread_.joinable())
                main_thread_.join();
            for (auto& side : side_threads_)
            {
                if (side.joinable())
                    side.join();
            }
            if (listen_ != INVALID_SOCKET)
                closesocket(listen_);
#if defined(_WIN32)
            WSACleanup();
#endif
        }

        WebSocketTestServer(const WebSocketTestServer&) = delete;
        WebSocketTestServer& operator=(const WebSocketTestServer&) = delete;

        int Port() const { return port_; }

        // Upgrades completed so far.
        int Upgrades() const { return upgrades_.load(std::memory_order_acquire); }

        // Pong frames received from the client (answers to SendPing).
        int Pongs() const { return pongs_.load(std::memory_order_acquire); }

        // Payload of the last pong received.
        std::string LastPong() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return last_pong_;
        }

        // True while an upgraded connection is being served.
        bool Connected() const { return connected_.load(std::memory_order_acquire); }

        // Raw request head (request line + headers) of the last upgrade request.
        std::string HandshakeHead() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return handshake_head_;
        }

        // Raw request head of the last upgrade that was accepted. Unlike HandshakeHead it is not
        // replaced by a refused upgrade or by a request that was no upgrade at all.
        std::string AcceptedHandshakeHead() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return accepted_head_;
        }

        // Status codes of the close frames received from clients, in order (0 = no code).
        std::vector<int> CloseCodes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return close_codes_;
        }

        // Text messages received so far, in order.
        std::vector<std::string> Received() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return received_;
        }

        // Binary messages received on main connections so far, whole, in order. Outside session
        // mode, with `echo`, each is answered with the text "binary:<size>:<fnv1a64 hex>".
        std::vector<std::string> BinaryReceived() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return binary_received_;
        }

        // Data frames of main connections that were part of a fragmented message (not final, or a
        // continuation). A client that sends each message as one frame keeps this at 0.
        int DataFrameFragments() const { return data_frame_fragments_.load(std::memory_order_acquire); }

        // Server-initiated text message as ONE frame. False when no client is connected.
        bool SendText(const std::string& text) { return SendFrame(0x1, /*fin=*/true, text); }

        // The same text as ONE frame, written by a thread of the server so that the test thread can
        // read it meanwhile: a write larger than what the loopback socket buffers hold completes
        // only while the client reads. JoinPushes() waits for every push and says whether all of
        // them were written; the destructor joins any left.
        void PushText(const std::string& text)
        {
            push_threads_.emplace_back([this, text] {
                if (!SendText(text))
                    push_failures_.fetch_add(1, std::memory_order_release);
            });
        }

        bool JoinPushes()
        {
            for (auto& push : push_threads_)
            {
                if (push.joinable())
                    push.join();
            }
            push_threads_.clear();
            return push_failures_.load(std::memory_order_acquire) == 0;
        }

        // The same text split into two frames (first not final, then a continuation).
        bool SendFragmented(const std::string& first, const std::string& second)
        {
            return SendFrame(0x1, /*fin=*/false, first) && SendFrame(0x0, /*fin=*/true, second);
        }

        // Only the first fragment of a text message: the message stays open until a final
        // continuation follows (SendContinuation).
        bool SendFragmentStart(const std::string& text) { return SendFrame(0x1, /*fin=*/false, text); }

        // Only the START of a final continuation frame that declares `declared_bytes` of payload.
        bool SendOversizedContinuationStart(uint64_t declared_bytes)
        {
            std::string frame;
            frame.push_back(static_cast<char>(0x80));
            frame.push_back(static_cast<char>(127));
            for (int shift = 56; shift >= 0; shift -= 8)
                frame.push_back(static_cast<char>((declared_bytes >> shift) & 0xFF));
            frame += "only-the-first-bytes";

            std::lock_guard<std::mutex> guard(send_mutex_);
            if (connection_ == INVALID_SOCKET)
                return false;
            return WriteAll(connection_, frame);
        }

        // A final continuation frame with no message in progress (a protocol violation).
        bool SendContinuation(const std::string& text) { return SendFrame(0x0, /*fin=*/true, text); }

        bool SendPing() { return SendFrame(0x9, /*fin=*/true, "ping"); }

        // One ping whose bytes reach the client in two pieces, a pause apart: the frame header
        // with the first half of the payload, then the rest. A client must answer it with ONE
        // pong carrying the whole payload.
        bool SendPingInTwoPieces(const std::string& payload)
        {
            if (payload.size() < 2 || payload.size() > 125)
                return false;

            std::string frame;
            frame.push_back(static_cast<char>(0x89));
            frame.push_back(static_cast<char>(payload.size()));
            frame += payload;
            const size_t cut = 2 + payload.size() / 2;

            std::lock_guard<std::mutex> guard(send_mutex_);
            if (connection_ == INVALID_SOCKET || !WriteAll(connection_, frame.substr(0, cut)))
                return false;
            std::this_thread::sleep_for(std::chrono::milliseconds(150));
            return WriteAll(connection_, frame.substr(cut));
        }

        // A ping whose payload is one byte over what a control frame may carry (RFC 6455 5.5).
        bool SendOversizedPing() { return SendFrame(0x9, /*fin=*/true, std::string(126, 'p')); }

        // A binary message as one frame.
        bool SendBinary(const std::string& bytes) { return SendFrame(0x2, /*fin=*/true, bytes); }

        // Only the START of a text frame: a header that declares `declared_bytes` of payload,
        // followed by a handful of them. A client with a message limit must refuse it from the
        // header, without waiting for (or buffering) the rest.
        bool SendOversizedFrameStart(uint64_t declared_bytes)
        {
            std::string frame;
            frame.push_back(static_cast<char>(0x81));
            frame.push_back(static_cast<char>(127));
            for (int shift = 56; shift >= 0; shift -= 8)
                frame.push_back(static_cast<char>((declared_bytes >> shift) & 0xFF));
            frame += "only-the-first-bytes";

            std::lock_guard<std::mutex> guard(send_mutex_);
            if (connection_ == INVALID_SOCKET)
                return false;
            return WriteAll(connection_, frame);
        }

        // Starts the close handshake from the server side with the given status code and reason.
        bool SendClose(uint16_t code, const std::string& reason = {}) { return SendFrame(0x8, /*fin=*/true, ClosePayload(code, reason)); }

        // Session mode, changeable while the server runs.
        // Refuse upgrades with this status from now on (0 = accept them).
        void RefuseUpgradesWith(int status) { refuse_status_.store(status, std::memory_order_release); }
        // The status of the acks from now on (200 by default).
        void AckWith(int status) { ack_status_.store(status, std::memory_order_release); }
        // Accept this many more `list` frames, then answer every later one with 503. A negative
        // number lifts the limit.
        void AcceptListFrames(int count) { list_budget_.store(count, std::memory_order_release); }
        // The statuses of the next `commands` acks, one per frame in order; after them, AckWith's.
        void AckNextCommandFramesWith(std::vector<int> statuses)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            command_acks_ = std::move(statuses);
        }
        // Stop (or resume) acknowledging requests; they are still received and recorded.
        void StaySilent(bool silent) { silent_.store(silent, std::memory_order_release); }
        // Application-level pings ({"op":"ping"}) received.
        int SessionPings() const { return session_pings_.load(std::memory_order_acquire); }

        // Drops the TCP connection without a close frame.
        void Drop() { drop_.store(true, std::memory_order_release); }

        // The sessionId the hellos of the sessions accepted from now on state. Without a call each
        // hello gets a new one, 32 hex digits. An empty `id` leaves the field out (a server before
        // #1845); any other text is sent as it is, malformed or not.
        void SetHelloSessionId(std::string id)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            scripted_session_id_ = true;
            session_id_ = std::move(id);
        }

        // The sessionId each session hello carried so far, in order ("" when it had none).
        std::vector<std::string> HelloSessionIds() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return hello_session_ids_;
        }

        // --- Side connections (Options::side_subprotocol), the second connection of #1848. ---

        // Refuse side upgrades with this status from now on (0 = accept them, selecting the side
        // subprotocol). Main upgrades are not affected.
        void RefuseSideUpgradesWith(int status) { side_refuse_status_.store(status, std::memory_order_release); }

        // The text message sent right after each side upgrade accepted from now on (its hello);
        // empty = none.
        void SetSideHello(std::string text)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            side_hello_ = std::move(text);
        }

        // Answer every binary message of a side connection with a text ack (the default) or not:
        // {"ack":N,"status":200,"body":{"bytes":SIZE,"fnv1a":"HEX"}}, N counting the binary
        // messages of that connection from 1.
        void AckSideBinary(bool ack) { side_ack_binary_.store(ack, std::memory_order_release); }

        // Raw request head of every side upgrade so far, accepted or refused, in order.
        std::vector<std::string> SideUpgradeHeads() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return side_heads_;
        }

        int SideUpgrades() const { return side_upgrades_.load(std::memory_order_acquire); }
        int SideRefusedUpgrades() const { return side_refused_.load(std::memory_order_acquire); }

        // True while a side connection is being served.
        bool SideConnected() const { return side_open_.load(std::memory_order_acquire) > 0; }

        // Binary messages received on side connections, whole, in order.
        std::vector<std::string> SideBinaryReceived() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return side_binary_;
        }

        // Text messages received on side connections, in order.
        std::vector<std::string> SideTextReceived() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return side_text_;
        }

        // Status codes of the close frames side clients sent, in order (0 = no code).
        std::vector<int> SideCloseCodes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return side_close_codes_;
        }

        // Writes to the latest side connection. False when none is open.
        bool SideSendText(const std::string& text) { return SendFrameOn(side_send_mutex_, side_connection_, 0x1, /*fin=*/true, text); }
        bool SideSendBinary(const std::string& bytes) { return SendFrameOn(side_send_mutex_, side_connection_, 0x2, /*fin=*/true, bytes); }

        // Starts the close handshake on the latest side connection with this code and reason.
        bool SideSendClose(uint16_t code, const std::string& reason = {})
        {
            return SendFrameOn(side_send_mutex_, side_connection_, 0x8, /*fin=*/true, ClosePayload(code, reason));
        }

        // Drops the TCP connection of the side connections without a close frame.
        void SideDrop() { side_drop_.store(true, std::memory_order_release); }

        // --- The scripted side of a session, for the conformance driver (#1660 step 3). ---
        // tests/conformance/README.md, "Collector session", is the specification of these.

        // One request frame of a session and how it was answered: the ack's status, or 0 when it
        // was left without an ack.
        struct SessionRequest
        {
            std::string frame;
            int status = 0;
        };

        // Every request frame received so far (heartbeats are not requests), in order.
        std::vector<SessionRequest> SessionRequests() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return session_requests_;
        }

        // What the hello of the sessions accepted from now on states.
        void SetHello(int max_frame_bytes, int heartbeat_seconds)
        {
            hello_frame_bytes_.store(max_frame_bytes, std::memory_order_release);
            hello_heartbeat_seconds_.store(heartbeat_seconds, std::memory_order_release);
        }

        // The status of the acks of one op ("list" or "commands") from now on. It takes
        // precedence over AckWith, which sets the status of every op. A request of any other op
        // is not touched by it.
        void AckOpWith(const std::string& op, int status)
        {
            if (op == "list")
                list_ack_status_.store(status, std::memory_order_release);
            else if (op == "commands")
                commands_ack_status_.store(status, std::memory_order_release);
        }

        // The next `count` requests are recorded and get no ack.
        void StaySilentFor(int count) { silent_budget_.store(count, std::memory_order_release); }

        // Stop (or resume) answering application-level pings; they are still counted.
        void AnswerSessionPings(bool answer) { answer_pings_.store(answer, std::memory_order_release); }

        // Upgrade requests refused so far.
        int RefusedUpgrades() const { return refused_upgrades_.load(std::memory_order_acquire); }

        // Requests that were not a WebSocket upgrade: what a collector on a session must never send.
        int PlainHttpRequests() const { return plain_http_requests_.load(std::memory_order_acquire); }

        // How many times the accept loop found no connection waiting. Two more of these while no
        // connection is being served mean that everything that was in the listen backlog has
        // been read.
        uint64_t IdleAcceptPolls() const { return idle_accept_polls_.load(std::memory_order_acquire); }

        // One request that is no upgrade, made by the test itself: what PlainHttpRequests counts.
        // Returns true when the server has answered and closed, so the request is counted by then.
        // The server serves one connection at a time, so a request made while a session is open
        // is read only when that session has ended: the wait for the answer is bounded by
        // `answer_timeout_ms`, and a request that gets no answer by then returns false instead
        // of hanging the test. No passing case comes near the bound.
        bool SendPlainHttpRequest(int answer_timeout_ms = 10000) const
        {
            const socket_t sock = socket(AF_INET, SOCK_STREAM, 0);
            if (sock == INVALID_SOCKET)
                return false;

            sockaddr_in address{};
            address.sin_family = AF_INET;
            address.sin_port = htons(static_cast<uint16_t>(port_));
            address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);

            const bool sent = connect(sock, reinterpret_cast<const sockaddr*>(&address), sizeof(address)) == 0 &&
                              WriteAll(sock, "GET /api/sensors/list HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");

            bool answered = false;
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(answer_timeout_ms);
            char buffer[256];
            while (sent)
            {
                const auto left = std::chrono::duration_cast<std::chrono::microseconds>(deadline - std::chrono::steady_clock::now()).count();
                if (left <= 0)
                    break;

                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(sock, &readfds);
                timeval tv{};
                tv.tv_sec = static_cast<decltype(tv.tv_sec)>(left / 1000000);
                tv.tv_usec = static_cast<decltype(tv.tv_usec)>(left % 1000000);
#if defined(_WIN32)
                const int nfds = 0;
#else
                const int nfds = sock + 1;
#endif
                const int ready = select(nfds, &readfds, nullptr, nullptr, &tv);
                if (ready < 0)
                {
#if !defined(_WIN32)
                    if (errno == EINTR)
                        continue;
#endif
                    break;
                }
                if (ready == 0)
                    continue; // the deadline is checked at the top

                if (recv(sock, buffer, static_cast<int>(sizeof(buffer)), 0) <= 0)
                {
                    answered = true; // the server answered and closed
                    break;
                }
            }

            closesocket(sock);
            return sent && answered;
        }

        // --- The scripted table connection (#1848 part 4), what an HSM server does on the side
        // connections of hsm.tables.v1 (wire contract, "Table connection"; the managed counterpart is
        // ScriptedSessionServer.Tables.cs). On with EnableTableScript; Options::side_subprotocol must be
        // the table subprotocol. A side upgrade whose X-Hsm-Session names no live session is refused
        // with 409; an accepted one gets the table hello, every probe its ack, every block frame the
        // scripted answer (without a script: stored, or duplicate for a producer and sequence stored
        // before). When a session ends its table connections are closed with 1000 "its session ended".

        struct TableAnswer
        {
            enum class Kind
            {
                Store,       // stored (duplicate when stored before), `refuse` tables refused
                Status,      // a failure ack with `status` (and retryAfter when >= 0); nothing stored
                Text,        // `text` as the answer, "{id}" replaced by the request id
                Silent,      // no answer, nothing stored
                Close,       // the connection closed with `status` and `text`; nothing stored
                Binary,      // a binary frame instead of an answer
                RefuseAll,   // allRefused, every table with `text` as the reason; nothing stored
                DropSession, // the session cut without a close; its table connection then closed 1000
            };
            Kind kind = Kind::Store;
            int status = 0;
            int retry_after = -1;
            std::string text;
            bool silent = false; // with Store: stored, and no ack
            std::map<std::string, std::string> refuse;

            static TableAnswer Stored(std::map<std::string, std::string> refuse = {}) { return { Kind::Store, 0, -1, {}, false, std::move(refuse) }; }
            static TableAnswer StoredSilently() { return { Kind::Store, 0, -1, {}, true, {} }; }
            static TableAnswer WithStatus(int status, int retry_after = -1) { return { Kind::Status, status, retry_after, {}, false, {} }; }
            static TableAnswer WithText(std::string text) { return { Kind::Text, 0, -1, std::move(text), false, {} }; }
            static TableAnswer NoAnswer() { return { Kind::Silent, 0, -1, {}, false, {} }; }
            static TableAnswer CloseWith(int code, std::string reason = {}) { return { Kind::Close, code, -1, std::move(reason), false, {} }; }
            static TableAnswer BinaryFrame() { return { Kind::Binary, 0, -1, {}, false, {} }; }
            static TableAnswer RefuseAll(std::string reason = "SensorCreatedAfterBlock") { return { Kind::RefuseAll, 0, -1, std::move(reason), false, {} }; }
            static TableAnswer DropSession() { return { Kind::DropSession, 0, -1, {}, false, {} }; }
        };

        // One block frame as the server read it.
        struct TableFrameRecord
        {
            int connection = 0; // 1 = the first table connection accepted
            int64_t id = 0;
            std::string producer; // 16 bytes
            int64_t sequence = 0;
            std::string block; // the frame after its 40-byte header
            size_t frame_bytes = 0;
            std::string outcome;
            bool stored = false;
            std::map<std::string, std::string> refused; // by table name
        };

        // `table_names` reads the table names of a block in directory order (the test's block
        // reader); without one the acks list no refusals by index.
        void EnableTableScript(std::function<std::vector<std::string>(const std::string& block)> table_names)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            table_names_ = std::move(table_names);
            table_script_.store(true, std::memory_order_release);
        }

        // What the table hello of the connections accepted from now on states.
        void SetTableHello(int max_frame_bytes, int heartbeat_seconds, int max_rows = 8 * 1024 * 1024, int max_tables = 65536)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            table_max_frame_bytes_ = max_frame_bytes;
            table_heartbeat_seconds_ = heartbeat_seconds;
            table_max_rows_ = max_rows;
            table_max_tables_ = max_tables;
        }
        int TableMaxFrameBytes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return table_max_frame_bytes_;
        }
        void SetTableMaxFrameBytes(int bytes)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            table_max_frame_bytes_ = bytes;
        }
        // The table hello is sent as this text instead, when not empty (a broken server).
        void SetTableHelloOverride(std::string text)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            table_hello_override_ = std::move(text);
        }
        // The next block frame is answered as `answer` says (one per call, in order).
        void AnswerTableNext(TableAnswer answer)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            table_script_answers_.push_back(std::move(answer));
        }
        // Closes every open table connection from the server's side.
        void CloseTableConnections(uint16_t code, const std::string& reason = {})
        {
            std::lock_guard<std::mutex> guard(side_send_mutex_);
            for (auto& open : table_open_)
                if (open.second.sock != INVALID_SOCKET)
                {
                    open.second.closing = true;
                    WriteFrame(open.second.sock, 0x8, /*fin=*/true, ClosePayload(code, reason));
                }
        }
        int TableConnections() const { return side_upgrades_.load(std::memory_order_acquire); }
        int RefusedTableUpgrades() const { return side_refused_.load(std::memory_order_acquire); }
        int TablePings() const { return table_pings_.load(std::memory_order_acquire); }
        // The X-Hsm-Session of every table upgrade the server saw (accepted or refused), in order.
        std::vector<std::string> TableUpgradeSessionIds() const
        {
            std::vector<std::string> ids;
            for (const auto& head : SideUpgradeHeads())
                ids.push_back(ws_detail::HeaderValue(head, "x-hsm-session"));
            return ids;
        }
        std::vector<int64_t> TableProbes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return table_probes_;
        }
        std::vector<TableFrameRecord> TableFrames() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return table_frames_;
        }
        // True once the server decided to leave the block frame of request `id` without an answer.
        bool TableFrameUnanswered(int64_t id) const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return table_unanswered_.count(id) != 0;
        }
        // Answer the client's close of a table connection (the default) or not: a server that never
        // answers keeps a client's close linger waiting for its whole bound.
        void SetTableAnswerCloses(bool answer) { table_answer_closes_.store(answer, std::memory_order_release); }

        // --- The scripted file connection (#1887), what an HSM server does on the side connections of
        // hsm.files.v1 (wire contract, "File connection"). On with EnableFileScript; Options::side_subprotocol
        // must be "hsm.files.v1". A side upgrade whose X-Hsm-Session names no live session is refused with
        // 409; an accepted one gets the script's hello, every text and binary message the script's answer.
        // One file connection per session: a new one closes the old with 1000 "replaced by a new file
        // connection of its session", whose upload is given back before the new one's hello. When a
        // session ends its file connections are closed with 1000 "its session ended".

        void EnableFileScript(std::shared_ptr<FileSideHandler> script)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            file_handler_ = std::move(script);
            file_script_.store(true, std::memory_order_release);
        }
        // File connections being served now.
        int OpenFileConnections() const
        {
            std::lock_guard<std::mutex> guard(side_send_mutex_);
            return static_cast<int>(file_open_.size());
        }
        // Closes every open file connection from the server's side.
        void CloseFileConnections(uint16_t code, const std::string& reason = {})
        {
            std::lock_guard<std::mutex> guard(side_send_mutex_);
            for (auto& open : file_open_)
                if (!open.second.closing)
                {
                    open.second.closing = true;
                    WriteFrame(open.second.sock, 0x8, /*fin=*/true, ClosePayload(code, reason));
                }
        }

        // Runs on the server's thread when the client's close frame of a session arrives, before it
        // is answered.
        void SetSessionCloseReceived(std::function<void()> hook)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            session_close_received_ = std::move(hook);
        }

    private:
        // One frame as it came off the wire, unmasked.
        struct Frame
        {
            uint8_t opcode = 0;
            bool fin = true;
            std::string payload;
        };

        static std::string ClosePayload(uint16_t code, const std::string& reason)
        {
            std::string payload;
            payload.push_back(static_cast<char>((code >> 8) & 0xFF));
            payload.push_back(static_cast<char>(code & 0xFF));
            payload += reason;
            return payload;
        }

        static int CloseCode(const std::string& payload)
        {
            return payload.size() >= 2 ? (static_cast<int>(static_cast<uint8_t>(payload[0])) << 8) | static_cast<uint8_t>(payload[1]) : 0;
        }

        bool WaitReadable(socket_t sock) const { return WaitReadable(sock, drop_); }

        // Waits until `sock` is readable; false when the server stops or `drop` is raised.
        bool WaitReadable(socket_t sock, const std::atomic<bool>& drop) const
        {
            while (!stop_.load(std::memory_order_acquire) && !drop.load(std::memory_order_acquire))
            {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(sock, &readfds);

                timeval tv{};
                tv.tv_sec = 0;
                tv.tv_usec = 20000; // 20 ms, then re-check stop_/drop_

#if defined(_WIN32)
                const int nfds = 0;
#else
                const int nfds = sock + 1;
#endif
                const int ready = select(nfds, &readfds, nullptr, nullptr, &tv);
                if (ready < 0)
                {
#if !defined(_WIN32)
                    if (errno == EINTR)
                        continue;
#endif
                    return false;
                }
                if (ready > 0)
                    return true;
            }
            return false;
        }

        bool ReadExact(socket_t sock, char* out, size_t size) const { return ReadExact(sock, out, size, drop_); }

        bool ReadExact(socket_t sock, char* out, size_t size, const std::atomic<bool>& drop) const
        {
            size_t got = 0;
            while (got < size)
            {
                if (!WaitReadable(sock, drop))
                    return false;
                const int n = recv(sock, out + got, static_cast<int>(size - got), 0);
                if (n <= 0)
                    return false;
                got += static_cast<size_t>(n);
            }
            return true;
        }

        static bool WriteAll(socket_t sock, const std::string& data)
        {
            // A peer that has gone must be an error here, not a SIGPIPE that ends the test binary.
#if defined(MSG_NOSIGNAL)
            const int flags = MSG_NOSIGNAL;
#else
            const int flags = 0;
#endif
            size_t sent = 0;
            while (sent < data.size())
            {
                const int n = send(sock, data.data() + sent, static_cast<int>(data.size() - sent), flags);
                if (n <= 0)
                    return false;
                sent += static_cast<size_t>(n);
            }
            return true;
        }

        bool SendFrame(uint8_t opcode, bool fin, const std::string& payload) { return SendFrameOn(send_mutex_, connection_, opcode, fin, payload); }

        // Writes one frame to the connection in `slot`, under `guard` (which also guards the slot).
        static bool SendFrameOn(std::mutex& guard, const socket_t& slot, uint8_t opcode, bool fin, const std::string& payload)
        {
            std::lock_guard<std::mutex> lock(guard);
            if (slot == INVALID_SOCKET)
                return false;
            return WriteFrame(slot, opcode, fin, payload);
        }

        static bool WriteFrame(socket_t sock, uint8_t opcode, bool fin, const std::string& payload)
        {
            std::string frame;
            frame.push_back(static_cast<char>((fin ? 0x80 : 0x00) | opcode));
            if (payload.size() < 126)
            {
                frame.push_back(static_cast<char>(payload.size()));
            }
            else if (payload.size() <= 0xFFFF)
            {
                frame.push_back(static_cast<char>(126));
                frame.push_back(static_cast<char>((payload.size() >> 8) & 0xFF));
                frame.push_back(static_cast<char>(payload.size() & 0xFF));
            }
            else
            {
                frame.push_back(static_cast<char>(127));
                const uint64_t size = payload.size();
                for (int shift = 56; shift >= 0; shift -= 8)
                    frame.push_back(static_cast<char>((size >> shift) & 0xFF));
            }
            frame += payload; // server frames are never masked
            return WriteAll(sock, frame);
        }

        // Reads one client frame. False when the connection ended, the server stops, `drop` is
        // raised, or the frame is unmasked or absurdly long (the connection is then given up).
        bool ReadFrame(socket_t conn, const std::atomic<bool>& drop, Frame& frame) const
        {
            char header[2];
            if (!ReadExact(conn, header, 2, drop))
                return false;

            frame.opcode = static_cast<uint8_t>(header[0]) & 0x0Fu;
            frame.fin = (static_cast<uint8_t>(header[0]) & 0x80u) != 0;
            const bool masked = (static_cast<uint8_t>(header[1]) & 0x80u) != 0;
            uint64_t length = static_cast<uint8_t>(header[1]) & 0x7Fu;

            if (length == 126)
            {
                char ext[2];
                if (!ReadExact(conn, ext, 2, drop))
                    return false;
                length = (static_cast<uint64_t>(static_cast<uint8_t>(ext[0])) << 8) | static_cast<uint8_t>(ext[1]);
            }
            else if (length == 127)
            {
                char ext[8];
                if (!ReadExact(conn, ext, 8, drop))
                    return false;
                length = 0;
                for (const char byte : ext)
                    length = (length << 8) | static_cast<uint8_t>(byte);
            }

            char mask[4] = { 0, 0, 0, 0 };
            if (masked && !ReadExact(conn, mask, 4, drop))
                return false;

            if (!masked || length > 64u * 1024u * 1024u)
                return false; // a client MUST mask (RFC 6455 5.1); refuse absurd lengths

            frame.payload.assign(static_cast<size_t>(length), '\0');
            if (length != 0 && !ReadExact(conn, &frame.payload[0], frame.payload.size(), drop))
                return false;
            for (size_t i = 0; i < frame.payload.size(); ++i)
                frame.payload[i] = static_cast<char>(frame.payload[i] ^ mask[i % 4]);
            return true;
        }

        void AcceptLoop()
        {
            while (!stop_.load(std::memory_order_acquire))
            {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(listen_, &readfds);

                timeval tv{};
                tv.tv_sec = 0;
                tv.tv_usec = 20000;

#if defined(_WIN32)
                const int nfds = 0;
#else
                const int nfds = listen_ + 1;
#endif
                const int ready = select(nfds, &readfds, nullptr, nullptr, &tv);
                if (ready < 0)
                {
#if !defined(_WIN32)
                    if (errno == EINTR)
                        continue;
#endif
                    return;
                }
                if (ready == 0)
                {
                    idle_accept_polls_.fetch_add(1, std::memory_order_release);
                    continue;
                }

                const socket_t conn = accept(listen_, nullptr, nullptr);
                if (conn == INVALID_SOCKET)
                    continue;

                if (options_.side_subprotocol.empty())
                {
                    drop_.store(false, std::memory_order_release);
                    ServeMain(conn, nullptr);
                    continue;
                }

                // Side connections are on: the request head decides which kind this one is.
                std::string head;
                if (!ReadHead(conn, head, never_dropped_))
                {
                    closesocket(conn);
                    continue;
                }
                if (ws_detail::Offers(ws_detail::HeaderValue(head, "sec-websocket-protocol"), options_.side_subprotocol))
                {
                    AcceptSide(conn, head);
                    continue;
                }
                // One main connection at a time, as without side connections.
                if (main_thread_.joinable())
                    main_thread_.join();
                drop_.store(false, std::memory_order_release);
                main_thread_ = std::thread([this, conn, head] { ServeMain(conn, &head); });
            }
        }

        // Serves one main connection to its end and releases it. `head`: its request head when it
        // was read already, null to read it here.
        void ServeMain(socket_t conn, const std::string* head)
        {
            Serve(conn, head);

            // As the server: the table connections of a session that ended are closed with 1000.
            std::string ended;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                ended.swap(live_session_);
            }
            if (table_script_.load(std::memory_order_acquire) && !ended.empty())
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                for (auto& open : table_open_)
                    if (open.second.session_id == ended && !open.second.closing)
                    {
                        open.second.closing = true;
                        WriteFrame(open.second.sock, 0x8, /*fin=*/true, ClosePayload(1000, "its session ended"));
                    }
            }
            if (file_script_.load(std::memory_order_acquire) && !ended.empty())
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                for (auto& open : file_open_)
                    if (open.second.session_id == ended && !open.second.closing)
                    {
                        open.second.closing = true;
                        WriteFrame(open.second.sock, 0x8, /*fin=*/true, ClosePayload(1000, "its session ended"));
                    }
            }

            {
                std::lock_guard<std::mutex> guard(send_mutex_);
                connection_ = INVALID_SOCKET;
            }
            connected_.store(false, std::memory_order_release);
            closesocket(conn);
        }

        // Reads an HTTP request head up to its blank line.
        bool ReadHead(socket_t conn, std::string& head, const std::atomic<bool>& drop) const
        {
            char c = 0;
            while (head.size() < 16384 && head.find("\r\n\r\n") == std::string::npos)
            {
                if (!ReadExact(conn, &c, 1, drop))
                    return false;
                head.push_back(c);
            }
            return true;
        }

        // Answers a side upgrade by the side script; an accepted one is served on its own thread.
        void AcceptSide(socket_t conn, const std::string& head)
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                side_heads_.push_back(head);
            }
            const std::string key = ws_detail::HeaderValue(head, "sec-websocket-key");
            int refuse = side_refuse_status_.load(std::memory_order_acquire);
            const std::string named = ws_detail::HeaderValue(head, "x-hsm-session");
            if (refuse == 0 && (table_script_.load(std::memory_order_acquire) || file_script_.load(std::memory_order_acquire)))
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (named.empty() || named != live_session_)
                    refuse = 409;
            }
            if (refuse != 0 || key.empty())
            {
                side_refused_.fetch_add(1, std::memory_order_release);
                WriteAll(conn, "HTTP/1.1 " + std::to_string(refuse != 0 ? refuse : 400) + " Refused\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                closesocket(conn);
                return;
            }
            if (!WriteAll(conn, "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " +
                                    ws_detail::AcceptKey(key) + "\r\nSec-WebSocket-Protocol: " + options_.side_subprotocol + "\r\n\r\n"))
            {
                closesocket(conn);
                return;
            }
            side_drop_.store(false, std::memory_order_release);
            side_open_.fetch_add(1, std::memory_order_release);
            if (file_script_.load(std::memory_order_acquire))
            {
                side_threads_.emplace_back([this, conn, named] { ServeFile(conn, named); });
                return;
            }
            if (table_script_.load(std::memory_order_acquire))
            {
                side_threads_.emplace_back([this, conn, named] { ServeTable(conn, named); });
                return;
            }
            side_threads_.emplace_back([this, conn] { ServeSide(conn); });
        }

        // The scripted table connection: hello, probes, block frames and their answers.
        void ServeTable(socket_t conn, const std::string& session_id)
        {
            const int index = side_upgrades_.fetch_add(1, std::memory_order_acq_rel) + 1;
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                side_connection_ = conn;
                table_open_[index] = TableOpen{ conn, session_id, false };
            }
            const auto reply = [this, conn](uint8_t opcode, const std::string& payload) {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                return WriteFrame(conn, opcode, /*fin=*/true, payload);
            };
            const auto close_with = [this, conn, index](uint16_t code, const std::string& reason) {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                table_open_[index].closing = true;
                return WriteFrame(conn, 0x8, /*fin=*/true, ClosePayload(code, reason));
            };

            std::string hello;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                hello = !table_hello_override_.empty()
                            ? table_hello_override_
                            : "{\"op\":\"hello\",\"body\":{\"maxFrameBytes\":" + std::to_string(table_max_frame_bytes_) +
                                  ",\"heartbeatSeconds\":" + std::to_string(table_heartbeat_seconds_) +
                                  ",\"blockLimits\":{\"maxDecompressedBytes\":67108864,\"maxTables\":" + std::to_string(table_max_tables_) +
                                  ",\"maxRows\":" + std::to_string(table_max_rows_) + ",\"maxColumnsPerTable\":64,\"maxTableIdLength\":1024}}}";
            }
            reply(0x1, hello);

            std::string message;
            uint8_t message_opcode = 0;
            Frame frame;
            bool open = true;
            while (open && ReadFrame(conn, side_drop_, frame))
            {
                if (frame.opcode == 0x8)
                {
                    bool closing = false;
                    {
                        std::lock_guard<std::mutex> guard(side_send_mutex_);
                        closing = table_open_[index].closing;
                    }
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        side_close_codes_.push_back(CloseCode(frame.payload));
                    }
                    if (!table_answer_closes_.load(std::memory_order_acquire))
                        continue; // read on, unanswered, until the client lets the connection go
                    if (!closing)
                        reply(0x8, frame.payload.substr(0, 2));
                    break;
                }
                if (frame.opcode == 0x9)
                {
                    reply(0xA, frame.payload);
                    continue;
                }
                if (frame.opcode == 0xA)
                    continue;
                if (frame.opcode != 0x0)
                {
                    message_opcode = frame.opcode;
                    message.swap(frame.payload);
                }
                else
                {
                    message += frame.payload;
                }
                if (!frame.fin)
                    continue;

                if (message_opcode == 0x1)
                {
                    if (message.find("\"ping\"") != std::string::npos)
                    {
                        table_pings_.fetch_add(1, std::memory_order_acq_rel);
                        reply(0x1, "{\"op\":\"pong\"}");
                    }
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        side_text_.push_back(message);
                    }
                }
                else if (message_opcode == 0x2)
                {
                    open = AnswerTableFrame(index, message, reply, close_with);
                }
                message.clear();
                message_opcode = 0;
            }

            // A closing connection is read on until the client's close (or its end).
            while (!open && ReadFrame(conn, side_drop_, frame))
            {
                if (frame.opcode == 0x8)
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    side_close_codes_.push_back(CloseCode(frame.payload));
                    break;
                }
            }

            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                if (side_connection_ == conn)
                    side_connection_ = INVALID_SOCKET;
                table_open_.erase(index);
            }
            side_open_.fetch_sub(1, std::memory_order_release);
            closesocket(conn);
        }

        // The scripted file connection: the script's hello, then every message to the script and its
        // answer back. Nothing the script throws leaves this thread.
        void ServeFile(socket_t conn, const std::string& session_id)
        {
            const int index = side_upgrades_.fetch_add(1, std::memory_order_acq_rel) + 1;
            std::shared_ptr<FileSideHandler> script;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                script = file_handler_;
            }
            // A new file connection of a session replaces the old one, which gives its upload back
            // before this one's hello.
            std::vector<int> replaced;
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                side_connection_ = conn;
                for (auto& open : file_open_)
                {
                    if (open.second.session_id != session_id)
                        continue;
                    replaced.push_back(open.first);
                    if (!open.second.closing)
                    {
                        open.second.closing = true;
                        WriteFrame(open.second.sock, 0x8, /*fin=*/true, ClosePayload(1000, "replaced by a new file connection of its session"));
                    }
                }
                file_open_[index] = TableOpen{ conn, session_id, false };
            }
            const auto reply = [this, conn](uint8_t opcode, const std::string& payload) {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                return WriteFrame(conn, opcode, /*fin=*/true, payload);
            };
            const auto close_with = [this, conn, index](uint16_t code, const std::string& reason) {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                file_open_[index].closing = true;
                return WriteFrame(conn, 0x8, /*fin=*/true, ClosePayload(code, reason));
            };
            const auto release = [&script](int connection) {
                try
                {
                    if (script)
                        script->Released(connection);
                }
                catch (...)
                {
                }
            };

            for (const int old : replaced)
                release(old);
            std::string hello;
            try
            {
                if (script)
                {
                    script->Opened(index, session_id);
                    hello = script->HelloText();
                }
            }
            catch (...)
            {
                hello.clear();
            }
            if (!hello.empty())
                reply(0x1, hello);

            std::string message;
            uint8_t message_opcode = 0;
            Frame frame;
            bool open = true;
            bool dropped = false;
            while (open && ReadFrame(conn, side_drop_, frame))
            {
                if (frame.opcode == 0x8)
                {
                    bool closing = false;
                    {
                        std::lock_guard<std::mutex> guard(side_send_mutex_);
                        closing = file_open_[index].closing;
                    }
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        side_close_codes_.push_back(CloseCode(frame.payload));
                    }
                    if (!closing)
                        reply(0x8, frame.payload.substr(0, 2));
                    break;
                }
                if (frame.opcode == 0x9)
                {
                    reply(0xA, frame.payload);
                    continue;
                }
                if (frame.opcode == 0xA)
                    continue;
                if (frame.opcode != 0x0)
                {
                    message_opcode = frame.opcode;
                    message.swap(frame.payload);
                }
                else
                {
                    message += frame.payload;
                }
                if (!frame.fin)
                    continue;

                FileSideAction action;
                try
                {
                    if (script && message_opcode == 0x1)
                        action = script->OnText(index, message);
                    else if (script && message_opcode == 0x2)
                        action = script->OnBinary(index, message);
                }
                catch (...)
                {
                    action = FileSideAction::CloseWith(1011, "the script failed");
                }
                if (message_opcode == 0x1)
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    side_text_.push_back(message);
                }
                message.clear();
                message_opcode = 0;

                switch (action.kind)
                {
                case FileSideAction::Kind::None:
                    break;
                case FileSideAction::Kind::Reply:
                    reply(0x1, action.text);
                    break;
                case FileSideAction::Kind::Close:
                    close_with(static_cast<uint16_t>(action.code), action.reason);
                    open = false;
                    break;
                case FileSideAction::Kind::Drop:
                    dropped = true;
                    open = false;
                    break;
                }
            }

            // A closing connection is read on until the client's close (or its end).
            while (!open && !dropped && ReadFrame(conn, side_drop_, frame))
            {
                if (frame.opcode == 0x8)
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    side_close_codes_.push_back(CloseCode(frame.payload));
                    break;
                }
            }

            // The upload is given back before the client can see the connection end.
            release(index);
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                if (side_connection_ == conn)
                    side_connection_ = INVALID_SOCKET;
                file_open_.erase(index);
            }
            side_open_.fetch_sub(1, std::memory_order_release);
            closesocket(conn);
        }

        static int64_t ReadLittleEndian(const std::string& bytes, size_t offset, size_t size)
        {
            uint64_t value = 0;
            for (size_t i = size; i > 0; --i)
                value = (value << 8) | static_cast<uint8_t>(bytes[offset + i - 1]);
            return static_cast<int64_t>(value);
        }

        // The ack of a stored block with the refusals of its first put (a duplicate answers the
        // first one's).
        std::string BlockAck(int64_t id, const std::string& outcome, const TableFrameRecord& stored, const std::vector<std::string>& names) const
        {
            std::string list;
            int refused = 0;
            for (size_t i = 0; i < names.size(); ++i)
            {
                const auto found = stored.refused.find(names[i]);
                if (found == stored.refused.end())
                    continue;
                list += (refused == 0 ? "" : ",") + std::string("{\"index\":") + std::to_string(i) + ",\"table\":\"" + names[i] +
                        "\",\"reason\":\"" + found->second + "\"}";
                ++refused;
            }
            const bool all = refused == static_cast<int>(names.size()) && !names.empty();
            return "{\"ack\":" + std::to_string(id) + ",\"status\":200,\"body\":{\"outcome\":\"" + (all && outcome == "stored" ? std::string("allRefused") : outcome) +
                   "\",\"tables\":" + std::to_string(names.size()) + ",\"refusedCount\":" + std::to_string(refused) + ",\"refused\":[" + list + "]}}";
        }

        // False when the connection is over (closing).
        template <class Reply, class Close>
        bool AnswerTableFrame(int index, const std::string& frame, const Reply& reply, const Close& close_with)
        {
            if (frame.size() < 40 || frame[0] != 1 || (frame[1] != 1 && frame[1] != 2) || frame[2] != 0 || frame[3] != 0 || ReadLittleEndian(frame, 4, 4) != 40)
            {
                close_with(1002, "a binary frame without a trusted header");
                return false;
            }
            const int64_t id = ReadLittleEndian(frame, 8, 8);
            if (frame[1] == 2)
            {
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    table_probes_.push_back(id);
                }
                reply(0x1, "{\"ack\":" + std::to_string(id) + ",\"status\":200,\"body\":{}}");
                return true;
            }

            TableFrameRecord record;
            record.connection = index;
            record.id = id;
            record.producer = frame.substr(16, 16);
            record.sequence = ReadLittleEndian(frame, 32, 8);
            record.block = frame.substr(40);
            record.frame_bytes = frame.size();

            TableAnswer answer = TableAnswer::Stored();
            std::function<std::vector<std::string>(const std::string&)> names_of;
            int max_frame = 0;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                max_frame = table_max_frame_bytes_;
                if (!table_script_answers_.empty())
                {
                    answer = std::move(table_script_answers_.front());
                    table_script_answers_.pop_front();
                }
                names_of = table_names_;
            }
            if (static_cast<int64_t>(frame.size()) > max_frame)
            {
                close_with(1009, "a frame over maxFrameBytes");
                return false;
            }
            std::vector<std::string> names;
            if (names_of)
            {
                try
                {
                    names = names_of(record.block);
                }
                catch (...)
                {
                    names.clear();
                }
            }
            const std::string key = record.producer + "/" + std::to_string(record.sequence);
            const std::string sid = std::to_string(id);

            const auto keep = [this](TableFrameRecord record_to_keep) {
                std::lock_guard<std::mutex> guard(mutex_);
                table_frames_.push_back(std::move(record_to_keep));
            };
            const auto unanswered = [this, id] {
                std::lock_guard<std::mutex> guard(mutex_);
                table_unanswered_.insert(id);
            };

            switch (answer.kind)
            {
            case TableAnswer::Kind::Store: {
                TableFrameRecord first;
                bool before = false;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    const auto found = table_stored_.find(key);
                    if (found != table_stored_.end())
                    {
                        before = true;
                        first = found->second;
                    }
                }
                if (before)
                {
                    record.outcome = "duplicate";
                    keep(record);
                    reply(0x1, BlockAck(id, "duplicate", first, names));
                    return true;
                }
                record.refused = answer.refuse;
                record.outcome = "stored";
                record.stored = true;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    table_stored_[key] = record;
                }
                keep(record);
                if (answer.silent)
                {
                    unanswered();
                    return true;
                }
                reply(0x1, BlockAck(id, "stored", record, names));
                return true;
            }
            case TableAnswer::Kind::Text: {
                std::string text = answer.text;
                for (size_t at = text.find("{id}"); at != std::string::npos; at = text.find("{id}", at + sid.size()))
                    text.replace(at, 4, sid);
                record.outcome = "text";
                keep(record);
                reply(0x1, text);
                return true;
            }
            case TableAnswer::Kind::Status:
                record.outcome = std::to_string(answer.status);
                keep(record);
                reply(0x1, "{\"ack\":" + sid + ",\"status\":" + std::to_string(answer.status) + ",\"error\":\"scripted\"" +
                               (answer.retry_after >= 0 ? ",\"retryAfter\":" + std::to_string(answer.retry_after) : std::string()) + "}");
                return true;
            case TableAnswer::Kind::Silent:
                record.outcome = "no answer";
                keep(record);
                unanswered();
                return true;
            case TableAnswer::Kind::Close:
                record.outcome = "closed " + std::to_string(answer.status);
                keep(record);
                close_with(static_cast<uint16_t>(answer.status), answer.text);
                return false;
            case TableAnswer::Kind::Binary:
                record.outcome = "binary";
                keep(record);
                reply(0x2, std::string("\x01\x02\x03", 3));
                return true;
            case TableAnswer::Kind::RefuseAll: {
                record.outcome = "allRefused";
                for (const auto& name : names)
                    record.refused[name] = answer.text;
                keep(record);
                reply(0x1, BlockAck(id, "allRefused", record, names));
                return true;
            }
            case TableAnswer::Kind::DropSession:
                record.outcome = "session dropped";
                keep(record);
                Drop();
                return true;
            }
            return true;
        }

        void ServeSide(socket_t conn)
        {
            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                side_connection_ = conn;
            }
            side_upgrades_.fetch_add(1, std::memory_order_release);

            std::string hello;
            {
                std::lock_guard<std::mutex> guard(mutex_);
                hello = side_hello_;
            }
            // This thread's own writes go to its own socket, under the side send lock.
            const auto reply = [this, conn](uint8_t opcode, const std::string& payload) {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                return WriteFrame(conn, opcode, /*fin=*/true, payload);
            };
            if (!hello.empty())
                reply(0x1, hello);

            uint64_t binary_count = 0;
            std::string message;
            uint8_t message_opcode = 0; // of the fragmented message in progress, 0 = none
            Frame frame;
            while (ReadFrame(conn, side_drop_, frame))
            {
                if (frame.opcode == 0x8)
                {
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        side_close_codes_.push_back(CloseCode(frame.payload));
                    }
                    reply(0x8, frame.payload.substr(0, 2));
                    break;
                }
                if (frame.opcode == 0x9)
                {
                    reply(0xA, frame.payload);
                    continue;
                }
                if (frame.opcode == 0xA)
                    continue;

                if (frame.opcode != 0x0)
                {
                    message_opcode = frame.opcode;
                    message.swap(frame.payload);
                }
                else
                {
                    message += frame.payload;
                }
                if (!frame.fin)
                    continue;

                if (message_opcode == 0x2)
                {
                    ++binary_count;
                    const uint64_t checksum = ws_detail::Fnv1a64(message);
                    const size_t size = message.size();
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        side_binary_.push_back(std::move(message));
                    }
                    if (side_ack_binary_.load(std::memory_order_acquire))
                        reply(0x1, "{\"ack\":" + std::to_string(binary_count) + ",\"status\":200,\"body\":{\"bytes\":" + std::to_string(size) +
                                       ",\"fnv1a\":\"" + ws_detail::Hex64(checksum) + "\"}}");
                }
                else
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    side_text_.push_back(std::move(message));
                }
                message.clear();
                message_opcode = 0;
            }

            {
                std::lock_guard<std::mutex> guard(side_send_mutex_);
                if (side_connection_ == conn)
                    side_connection_ = INVALID_SOCKET;
            }
            side_open_.fetch_sub(1, std::memory_order_release);
            closesocket(conn);
        }

        // Answers the HTTP upgrade request `head` of a main connection. True when the connection
        // was upgraded.
        bool Handshake(socket_t conn, const std::string& head)
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                handshake_head_ = head;
            }

            const std::string key = ws_detail::HeaderValue(head, "sec-websocket-key");
            const int refuse = refuse_status_.load(std::memory_order_acquire);
            if (key.empty())
                plain_http_requests_.fetch_add(1, std::memory_order_release);
            else if (refuse != 0)
                refused_upgrades_.fetch_add(1, std::memory_order_release);
            if (refuse != 0 || key.empty())
            {
                const int status = refuse != 0 ? refuse : 400;
                WriteAll(conn, "HTTP/1.1 " + std::to_string(status) + " Refused\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return false;
            }

            std::string response;
            if (options_.interim_status != 0)
                response += "HTTP/1.1 " + std::to_string(options_.interim_status) + " Interim\r\nLink: </style.css>; rel=preload\r\n\r\n";
            response += "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " +
                        ws_detail::AcceptKey(key) + "\r\n";
            if (!options_.subprotocol.empty())
                response += "Sec-WebSocket-Protocol: " + options_.subprotocol + "\r\n";
            response += "\r\n";
            if (!WriteAll(conn, response))
                return false;

            std::lock_guard<std::mutex> guard(mutex_);
            accepted_head_ = head;
            return true;
        }

        void Serve(socket_t conn, const std::string* read_head)
        {
            std::string head;
            if (read_head != nullptr)
                head = *read_head;
            else if (!ReadHead(conn, head, drop_))
                return;
            if (!Handshake(conn, head))
                return;

            {
                std::lock_guard<std::mutex> guard(send_mutex_);
                connection_ = conn;
            }
            upgrades_.fetch_add(1, std::memory_order_release);
            connected_.store(true, std::memory_order_release);

            if (options_.session)
            {
                const int scripted_bytes = hello_frame_bytes_.load(std::memory_order_acquire);
                const int scripted_heartbeat = hello_heartbeat_seconds_.load(std::memory_order_acquire);
                std::string session_id;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    if (scripted_session_id_)
                    {
                        session_id = session_id_;
                    }
                    else
                    {
                        // A new id per session, 32 hex digits, as a server writes a Guid ("N").
                        const std::string serial = ws_detail::Hex64(hello_session_ids_.size() + 1);
                        session_id = "5e5510" + std::string(10, '0') + serial;
                    }
                    hello_session_ids_.push_back(session_id);
                    live_session_ = session_id;
                }
                SendFrame(0x1, /*fin=*/true,
                          "{\"op\":\"hello\",\"body\":{\"maxFrameBytes\":" +
                              std::to_string(scripted_bytes != 0 ? scripted_bytes : options_.max_frame_bytes) + ",\"heartbeatSeconds\":" +
                              std::to_string(scripted_heartbeat != 0 ? scripted_heartbeat : options_.heartbeat_seconds) +
                              (session_id.empty() ? std::string{} : ",\"sessionId\":\"" + session_id + "\"") + "}}");
            }

            std::string binary;     // the binary message being assembled from fragments
            bool in_binary = false; // a fragmented binary message is in progress
            Frame frame;
            while (ReadFrame(conn, drop_, frame))
            {
                std::string& payload = frame.payload;
                if (frame.opcode == 0x8) // close: record the code, complete the handshake and end
                {
                    std::function<void()> hook;
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        close_codes_.push_back(CloseCode(payload));
                        hook = session_close_received_;
                    }
                    if (hook)
                        hook();
                    SendFrame(0x8, /*fin=*/true, payload.substr(0, 2));
                    return;
                }
                if (frame.opcode == 0x9) // ping
                {
                    SendFrame(0xA, /*fin=*/true, payload);
                    continue;
                }
                if (frame.opcode == 0xA) // pong
                {
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        last_pong_ = payload;
                    }
                    pongs_.fetch_add(1, std::memory_order_release);
                    continue;
                }

                if (frame.opcode <= 0x2 && (frame.opcode == 0x0 || !frame.fin))
                    data_frame_fragments_.fetch_add(1, std::memory_order_release);

                // A binary message, whole once its final fragment is in.
                if (frame.opcode == 0x2 || (frame.opcode == 0x0 && in_binary))
                {
                    if (frame.opcode == 0x2)
                        binary.swap(payload);
                    else
                        binary += payload;
                    in_binary = !frame.fin;
                    if (in_binary)
                        continue;

                    const std::string answer = "binary:" + std::to_string(binary.size()) + ":" + ws_detail::Hex64(ws_detail::Fnv1a64(binary));
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        binary_received_.push_back(std::move(binary));
                    }
                    binary.clear();
                    if (!options_.session && options_.echo)
                        SendFrame(0x1, /*fin=*/true, answer);
                    continue;
                }

                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    if (!(options_.session && payload.find("\"op\":\"ping\"") != std::string::npos))
                        received_.push_back(payload);
                }
                if (options_.session)
                    AnswerSessionFrame(payload);
                else if (options_.echo)
                    SendFrame(0x1, /*fin=*/true, "echo:" + payload);
            }
        }

        // What an HSM server answers to one frame of a collector session.
        void AnswerSessionFrame(const std::string& payload)
        {
            if (payload.find("\"op\":\"ping\"") != std::string::npos)
            {
                session_pings_.fetch_add(1, std::memory_order_release);
                if (answer_pings_.load(std::memory_order_acquire))
                    SendFrame(0x1, /*fin=*/true, "{\"op\":\"pong\"}");
                return;
            }

            // The collector writes its frames as {"id":N,"op":...}: the id is the first number.
            const size_t at = payload.find("\"id\":");
            if (at == std::string::npos)
                return;

            bool silent = silent_.load(std::memory_order_acquire);
            if (!silent)
            {
                int budget = silent_budget_.load(std::memory_order_acquire);
                while (budget > 0 && !silent_budget_.compare_exchange_weak(budget, budget - 1, std::memory_order_acq_rel))
                {
                }
                silent = budget > 0;
            }
            if (silent)
            {
                std::lock_guard<std::mutex> guard(mutex_);
                session_requests_.push_back({ payload, 0 });
                return;
            }

            size_t end = at + 5;
            while (end < payload.size() && payload[end] >= 48 && payload[end] <= 57)
                ++end;
            const std::string id = payload.substr(at + 5, end - (at + 5));

            const bool is_list = payload.find("\"op\":\"list\"") != std::string::npos;
            const bool is_commands = !is_list && payload.find("\"op\":\"commands\"") != std::string::npos;
            int status = ack_status_.load(std::memory_order_acquire);
            const int op_status = is_list       ? list_ack_status_.load(std::memory_order_acquire)
                                  : is_commands ? commands_ack_status_.load(std::memory_order_acquire)
                                                : 0;
            if (op_status != 0)
                status = op_status;
            if (is_list)
            {
                int budget = list_budget_.load(std::memory_order_acquire);
                if (budget == 0)
                    status = 503;
                else if (budget > 0)
                    list_budget_.store(budget - 1, std::memory_order_release);
            }
            else if (is_commands)
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (!command_acks_.empty())
                {
                    status = command_acks_.front();
                    command_acks_.erase(command_acks_.begin());
                }
            }
            // Recorded with its status only once the ack is written: a test that sees a request as
            // accepted and then cuts the session must not race the ack it is counting on.
            const bool acked = SendFrame(0x1, /*fin=*/true,
                                         status >= 200 && status < 300 ? "{\"ack\":" + id + ",\"status\":" + std::to_string(status) + ",\"body\":{}}"
                                                                       : "{\"ack\":" + id + ",\"status\":" + std::to_string(status) + ",\"error\":\"refused by the test\"}");

            std::lock_guard<std::mutex> guard(mutex_);
            session_requests_.push_back({ payload, acked ? status : 0 });
        }

        Options options_;
        std::atomic<int> refuse_status_{ 0 };
        std::atomic<int> ack_status_{ 200 };
        std::atomic<bool> silent_{ false };
        std::atomic<int> list_budget_{ -1 };
        std::atomic<int> session_pings_{ 0 };
        std::atomic<int> hello_frame_bytes_{ 0 };       // 0 = options_.max_frame_bytes
        std::atomic<int> hello_heartbeat_seconds_{ 0 }; // 0 = options_.heartbeat_seconds
        std::atomic<int> list_ack_status_{ 0 };         // 0 = ack_status_
        std::atomic<int> commands_ack_status_{ 0 };     // 0 = ack_status_
        std::atomic<int> silent_budget_{ 0 };
        std::atomic<bool> answer_pings_{ true };
        std::atomic<int> refused_upgrades_{ 0 };
        std::atomic<int> plain_http_requests_{ 0 };
        std::atomic<uint64_t> idle_accept_polls_{ 0 };
        socket_t listen_ = INVALID_SOCKET;
        int port_ = 0;
        std::atomic<bool> stop_{ false };
        std::atomic<bool> drop_{ false };
        std::atomic<bool> connected_{ false };
        std::atomic<int> upgrades_{ 0 };
        std::atomic<int> pongs_{ 0 };
        std::atomic<int> data_frame_fragments_{ 0 };
        std::thread worker_;
        std::mutex send_mutex_;
        socket_t connection_ = INVALID_SOCKET; // guarded by send_mutex_
        mutable std::mutex mutex_;
        std::string accepted_head_;                    // guarded by mutex_
        std::string handshake_head_;                   // guarded by mutex_
        std::string last_pong_;                        // guarded by mutex_
        std::vector<std::string> received_;            // guarded by mutex_
        std::vector<int> command_acks_;                // guarded by mutex_
        std::vector<int> close_codes_;                 // guarded by mutex_
        std::vector<SessionRequest> session_requests_; // guarded by mutex_
        std::vector<std::string> binary_received_;     // guarded by mutex_
        bool scripted_session_id_ = false;             // guarded by mutex_
        std::string session_id_;                       // guarded by mutex_
        std::vector<std::string> hello_session_ids_;   // guarded by mutex_

        std::vector<std::thread> push_threads_; // PushText; touched by the test thread, then the destructor
        std::atomic<int> push_failures_{ 0 };

        // Side connections. The threads are started by the accept loop and joined by the destructor.
        const std::atomic<bool> never_dropped_{ false };
        std::thread main_thread_;               // the main connection when side connections are on
        std::vector<std::thread> side_threads_; // touched by the accept loop, then the destructor
        std::atomic<int> side_refuse_status_{ 0 };
        std::atomic<bool> side_ack_binary_{ true };
        std::atomic<bool> side_drop_{ false };
        std::atomic<int> side_upgrades_{ 0 };
        std::atomic<int> side_refused_{ 0 };
        std::atomic<int> side_open_{ 0 };
        mutable std::mutex side_send_mutex_;
        socket_t side_connection_ = INVALID_SOCKET; // the latest side connection; guarded by side_send_mutex_
        std::string side_hello_;                    // guarded by mutex_
        std::vector<std::string> side_heads_;       // guarded by mutex_
        std::vector<std::string> side_binary_;      // guarded by mutex_
        std::vector<std::string> side_text_;        // guarded by mutex_
        std::vector<int> side_close_codes_;         // guarded by mutex_

        // The table script.
        struct TableOpen
        {
            socket_t sock = INVALID_SOCKET;
            std::string session_id;
            bool closing = false; // the server sent its close
        };
        std::atomic<bool> table_script_{ false };
        std::atomic<int> table_pings_{ 0 };
        std::atomic<bool> table_answer_closes_{ true };
        std::map<int, TableOpen> table_open_;                                     // guarded by side_send_mutex_
        std::string live_session_;                                                // guarded by mutex_
        std::function<std::vector<std::string>(const std::string&)> table_names_; // guarded by mutex_
        int table_max_frame_bytes_ = 4 * 1024 * 1024;                             // guarded by mutex_
        int table_heartbeat_seconds_ = 30;                                        // guarded by mutex_
        int table_max_rows_ = 8 * 1024 * 1024;                                    // guarded by mutex_
        int table_max_tables_ = 65536;                                            // guarded by mutex_
        std::string table_hello_override_;                                        // guarded by mutex_
        std::deque<TableAnswer> table_script_answers_;                            // guarded by mutex_
        std::vector<int64_t> table_probes_;                                       // guarded by mutex_
        std::vector<TableFrameRecord> table_frames_;                              // guarded by mutex_
        std::map<std::string, TableFrameRecord> table_stored_;                    // guarded by mutex_
        std::set<int64_t> table_unanswered_;                                      // guarded by mutex_
        std::function<void()> session_close_received_;                            // guarded by mutex_

        // The file script.
        std::atomic<bool> file_script_{ false };
        std::shared_ptr<FileSideHandler> file_handler_; // guarded by mutex_
        std::map<int, TableOpen> file_open_;            // guarded by side_send_mutex_
    };
} // namespace hsm::test
