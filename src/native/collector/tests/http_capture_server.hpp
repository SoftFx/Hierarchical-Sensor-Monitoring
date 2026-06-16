#pragma once

// Minimal in-process HTTP capture server for the native HTTP-transport E2E tests (#1096 §12).
// Test-only; compiled only in the HSM_COLLECTOR_HTTP build. Blocking accept loop on a single
// worker thread (modelled on the .NET FakeHsmServer): captures one request's method/path/headers/
// body and answers a configurable status. No TLS — the E2E lane is plaintext, matching the .NET
// FakeServerE2ETests (AllowPlaintextTransport).

#include <atomic>
#include <cstdint>
#include <cstring>
#include <string>
#include <thread>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX // windows.h defines min/max macros that break std::min/std::max otherwise
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")
using socket_t = SOCKET;
#else
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
using socket_t = int;
#define INVALID_SOCKET (-1)
#define closesocket close
#endif

namespace hsm::test
{
    struct CapturedRequest
    {
        // Atomic release/acquire so the test thread reading the captured fields after observing
        // received == true has a happens-before with the worker thread that wrote them (TSan-clean).
        std::atomic<bool> received{ false };
        std::string method;
        std::string path;
        std::string headers; // raw header block (lowercased lookups done by the test)
        std::string body;
    };

    class HttpCaptureServer
    {
    public:
        explicit HttpCaptureServer(int response_status = 200)
            : response_status_(response_status)
        {
#if defined(_WIN32)
            WSADATA wsa;
            WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
            listen_ = socket(AF_INET, SOCK_STREAM, 0);

            sockaddr_in addr{};
            addr.sin_family = AF_INET;
            addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
            addr.sin_port = 0; // ephemeral

            int yes = 1;
            setsockopt(listen_, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&yes), sizeof(yes));
            bind(listen_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr));
            listen(listen_, 1);

            sockaddr_in bound{};
            socklen_t len = sizeof(bound);
            getsockname(listen_, reinterpret_cast<sockaddr*>(&bound), &len);
            port_ = ntohs(bound.sin_port);

            worker_ = std::thread([this] { AcceptOne(); });
        }

        ~HttpCaptureServer()
        {
            stop_.store(true);
            if (listen_ != INVALID_SOCKET)
                closesocket(listen_);
            if (worker_.joinable())
                worker_.join();
#if defined(_WIN32)
            WSACleanup();
#endif
        }

        int Port() const { return port_; }

        const CapturedRequest& Request() const { return request_; }

    private:
        void AcceptOne()
        {
            socket_t conn = accept(listen_, nullptr, nullptr);
            if (conn == INVALID_SOCKET)
                return;

            std::string raw;
            char buffer[4096];
            size_t header_end = std::string::npos;
            size_t content_length = 0;
            bool have_length = false;

            // Read until the headers are complete and the declared body has arrived.
            for (;;)
            {
                const int n = recv(conn, buffer, sizeof(buffer), 0);
                if (n <= 0)
                    break;
                raw.append(buffer, static_cast<size_t>(n));

                if (header_end == std::string::npos)
                {
                    header_end = raw.find("\r\n\r\n");
                    if (header_end != std::string::npos)
                    {
                        const auto cl = FindHeader(raw.substr(0, header_end), "content-length");
                        if (!cl.empty())
                        {
                            content_length = static_cast<size_t>(std::stoul(cl));
                            have_length = true;
                        }
                    }
                }

                if (header_end != std::string::npos)
                {
                    const size_t body_have = raw.size() - (header_end + 4);
                    if (!have_length || body_have >= content_length)
                        break;
                }
            }

            if (header_end != std::string::npos)
            {
                const std::string head = raw.substr(0, header_end);
                const size_t line_end = head.find("\r\n");
                const std::string request_line = head.substr(0, line_end);
                const size_t sp1 = request_line.find(' ');
                const size_t sp2 = request_line.find(' ', sp1 + 1);

                request_.method = request_line.substr(0, sp1);
                request_.path = request_line.substr(sp1 + 1, sp2 - sp1 - 1);
                request_.headers = head.substr(line_end + 2);
                request_.body = raw.substr(header_end + 4, have_length ? content_length : std::string::npos);
                request_.received.store(true, std::memory_order_release);
            }

            const std::string response =
                "HTTP/1.1 " + std::to_string(response_status_) + " X\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            send(conn, response.c_str(), static_cast<int>(response.size()), 0);
            closesocket(conn);
        }

        static std::string FindHeader(const std::string& headers, const std::string& name_lower)
        {
            std::string lower = headers;
            for (auto& c : lower)
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

            size_t pos = lower.find(name_lower + ":");
            if (pos == std::string::npos)
                return {};

            pos = headers.find(':', pos) + 1;
            const size_t line_end = headers.find("\r\n", pos);
            std::string value = headers.substr(pos, line_end - pos);
            const size_t first = value.find_first_not_of(" \t");
            return first == std::string::npos ? std::string{} : value.substr(first);
        }

        socket_t listen_ = INVALID_SOCKET;
        int port_ = 0;
        int response_status_;
        std::atomic<bool> stop_{ false };
        std::thread worker_;
        CapturedRequest request_;
    };
}
