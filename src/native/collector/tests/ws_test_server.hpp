#pragma once

// Minimal in-process WebSocket server for the native WebSocket tests (#1662). Test-only; compiled
// only in the HSM_COLLECTOR_HTTP build. Implements just enough of RFC 6455 to exercise a client:
// the upgrade handshake, masked client frames, unmasked server frames with 7/16/64-bit lengths,
// ping/pong and the close handshake. Plaintext only, like the HTTP capture servers next to it.
// Serves one connection at a time on its own thread.
//
// This is the only RFC 6455 code the repository owns, and it never ships: the collector's client
// side is libcurl's. Being a second, independent implementation is the point of it. Its handshake
// digest is pinned by the RFC's own example in native_ws_fixture_accept_key_matches_rfc.

#include "http_capture_server.hpp" // socket_t, platform socket headers

#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>
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
        };

        WebSocketTestServer()
            : WebSocketTestServer(Options{}) {}

        explicit WebSocketTestServer(Options options)
            : options_(std::move(options))
        {
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
            if (worker_.joinable())
                worker_.join();
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

        // True while an upgraded connection is being served.
        bool Connected() const { return connected_.load(std::memory_order_acquire); }

        // Raw request head (request line + headers) of the last upgrade request.
        std::string HandshakeHead() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return handshake_head_;
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

        // Server-initiated text message as ONE frame. False when no client is connected.
        bool SendText(const std::string& text) { return SendFrame(0x1, /*fin=*/true, text); }

        // The same text split into two frames (first not final, then a continuation).
        bool SendFragmented(const std::string& first, const std::string& second)
        {
            return SendFrame(0x1, /*fin=*/false, first) && SendFrame(0x0, /*fin=*/true, second);
        }

        // A final continuation frame with no message in progress (a protocol violation).
        bool SendContinuation(const std::string& text) { return SendFrame(0x0, /*fin=*/true, text); }

        bool SendPing() { return SendFrame(0x9, /*fin=*/true, "ping"); }

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

        // Starts the close handshake from the server side with the given status code.
        bool SendClose(uint16_t code)
        {
            std::string payload;
            payload.push_back(static_cast<char>((code >> 8) & 0xFF));
            payload.push_back(static_cast<char>(code & 0xFF));
            return SendFrame(0x8, /*fin=*/true, payload);
        }

        // Drops the TCP connection without a close frame.
        void Drop() { drop_.store(true, std::memory_order_release); }

    private:
        bool WaitReadable(socket_t sock) const
        {
            while (!stop_.load(std::memory_order_acquire) && !drop_.load(std::memory_order_acquire))
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

        bool ReadExact(socket_t sock, char* out, size_t size) const
        {
            size_t got = 0;
            while (got < size)
            {
                if (!WaitReadable(sock))
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
            size_t sent = 0;
            while (sent < data.size())
            {
                const int n = send(sock, data.data() + sent, static_cast<int>(data.size() - sent), 0);
                if (n <= 0)
                    return false;
                sent += static_cast<size_t>(n);
            }
            return true;
        }

        bool SendFrame(uint8_t opcode, bool fin, const std::string& payload)
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

            std::lock_guard<std::mutex> guard(send_mutex_);
            if (connection_ == INVALID_SOCKET)
                return false;
            return WriteAll(connection_, frame);
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
                    continue;

                const socket_t conn = accept(listen_, nullptr, nullptr);
                if (conn == INVALID_SOCKET)
                    continue;

                drop_.store(false, std::memory_order_release);
                Serve(conn);

                {
                    std::lock_guard<std::mutex> guard(send_mutex_);
                    connection_ = INVALID_SOCKET;
                }
                connected_.store(false, std::memory_order_release);
                closesocket(conn);
            }
        }

        // Reads the HTTP upgrade request and answers it. True when the connection was upgraded.
        bool Handshake(socket_t conn)
        {
            std::string head;
            char c = 0;
            while (head.size() < 16384 && head.find("\r\n\r\n") == std::string::npos)
            {
                if (!ReadExact(conn, &c, 1))
                    return false;
                head.push_back(c);
            }

            {
                std::lock_guard<std::mutex> guard(mutex_);
                handshake_head_ = head;
            }

            const std::string key = ws_detail::HeaderValue(head, "sec-websocket-key");
            if (options_.refuse_with_status != 0 || key.empty())
            {
                const int status = options_.refuse_with_status != 0 ? options_.refuse_with_status : 400;
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
            return WriteAll(conn, response);
        }

        void Serve(socket_t conn)
        {
            if (!Handshake(conn))
                return;

            {
                std::lock_guard<std::mutex> guard(send_mutex_);
                connection_ = conn;
            }
            upgrades_.fetch_add(1, std::memory_order_release);
            connected_.store(true, std::memory_order_release);

            for (;;)
            {
                char header[2];
                if (!ReadExact(conn, header, 2))
                    return;

                const uint8_t opcode = static_cast<uint8_t>(header[0]) & 0x0Fu;
                const bool masked = (static_cast<uint8_t>(header[1]) & 0x80u) != 0;
                uint64_t length = static_cast<uint8_t>(header[1]) & 0x7Fu;

                if (length == 126)
                {
                    char ext[2];
                    if (!ReadExact(conn, ext, 2))
                        return;
                    length = (static_cast<uint64_t>(static_cast<uint8_t>(ext[0])) << 8) | static_cast<uint8_t>(ext[1]);
                }
                else if (length == 127)
                {
                    char ext[8];
                    if (!ReadExact(conn, ext, 8))
                        return;
                    length = 0;
                    for (const char byte : ext)
                        length = (length << 8) | static_cast<uint8_t>(byte);
                }

                char mask[4] = { 0, 0, 0, 0 };
                if (masked && !ReadExact(conn, mask, 4))
                    return;

                if (!masked || length > 64u * 1024u * 1024u)
                    return; // a client MUST mask (RFC 6455 5.1); refuse absurd lengths

                std::string payload(static_cast<size_t>(length), '\0');
                if (length != 0 && !ReadExact(conn, &payload[0], payload.size()))
                    return;
                for (size_t i = 0; i < payload.size(); ++i)
                    payload[i] = static_cast<char>(payload[i] ^ mask[i % 4]);

                if (opcode == 0x8) // close: record the code, complete the handshake and end
                {
                    {
                        std::lock_guard<std::mutex> guard(mutex_);
                        close_codes_.push_back(payload.size() >= 2
                                                   ? (static_cast<int>(static_cast<uint8_t>(payload[0])) << 8) | static_cast<uint8_t>(payload[1])
                                                   : 0);
                    }
                    SendFrame(0x8, /*fin=*/true, payload.substr(0, 2));
                    return;
                }
                if (opcode == 0x9) // ping
                {
                    SendFrame(0xA, /*fin=*/true, payload);
                    continue;
                }
                if (opcode == 0xA) // pong
                {
                    pongs_.fetch_add(1, std::memory_order_release);
                    continue;
                }

                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    received_.push_back(payload);
                }
                if (options_.echo)
                    SendFrame(0x1, /*fin=*/true, "echo:" + payload);
            }
        }

        Options options_;
        socket_t listen_ = INVALID_SOCKET;
        int port_ = 0;
        std::atomic<bool> stop_{ false };
        std::atomic<bool> drop_{ false };
        std::atomic<bool> connected_{ false };
        std::atomic<int> upgrades_{ 0 };
        std::atomic<int> pongs_{ 0 };
        std::thread worker_;
        std::mutex send_mutex_;
        socket_t connection_ = INVALID_SOCKET; // guarded by send_mutex_
        mutable std::mutex mutex_;
        std::string handshake_head_;        // guarded by mutex_
        std::vector<std::string> received_; // guarded by mutex_
        std::vector<int> close_codes_;      // guarded by mutex_
    };
} // namespace hsm::test
