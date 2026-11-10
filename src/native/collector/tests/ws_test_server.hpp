#pragma once

// Minimal in-process WebSocket server for the native WebSocket tests (#1662). Test-only; compiled
// only in the HSM_COLLECTOR_HTTP build. Implements just enough of RFC 6455 to exercise a client:
// the upgrade handshake, masked client frames, unmasked server frames with 7/16/64-bit lengths,
// ping/pong and the close handshake. Plaintext only, like the HTTP capture servers next to it.
// Serves one connection at a time on its own thread.
//
// The FRAMING here is written separately from the client's codec (the server parses masked frames
// and writes unmasked ones, the client does the opposite), so each side checks the other. The
// handshake DIGEST is the codec's own AcceptKey: a second copy of SHA-1 would prove nothing, and
// the digest is pinned by published vectors in native_ws_codec_handshake_digest_matches_rfc.

#include "../src/hsm_ws_codec.hpp"
#include "http_capture_server.hpp" // socket_t, platform socket headers

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
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
            // Upgrade with a Sec-WebSocket-Accept that does not match the client's key.
            bool wrong_accept = false;
            // Send this interim response (e.g. 103) before the final one; 0 = none.
            int interim_status = 0;
            // Leave `Connection: Upgrade` out of the 101.
            bool omit_connection_header = false;
        };

        WebSocketTestServer() : WebSocketTestServer(Options{}) {}

        explicit WebSocketTestServer(Options options) : options_(std::move(options))
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
            response += std::string("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n") +
                (options_.omit_connection_header ? "" : "Connection: Upgrade\r\n") + "Sec-WebSocket-Accept: " +
                hsm::http::ws::AcceptKey(options_.wrong_accept ? key + "x" : key) + "\r\n";
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
