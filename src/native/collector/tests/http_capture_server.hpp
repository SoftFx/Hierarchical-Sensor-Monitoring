#pragma once

// Minimal in-process HTTP capture server for the native HTTP-transport E2E tests (#1096 §12).
// Test-only; compiled only in the HSM_COLLECTOR_HTTP build. Blocking accept loop on a single
// worker thread (modelled on the .NET FakeHsmServer): captures one request's method/path/headers/
// body and answers a configurable status. No TLS — the E2E lane is plaintext, matching the .NET
// FakeServerE2ETests (AllowPlaintextTransport).

#include <atomic>
#include <cstdint>
#include <cctype>
#include <cerrno> // EINTR retry in the accept loop
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

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
#include <sys/select.h>
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
        // `only_path`: capture the first request to this path; answer every other request with
        // `response_status` and keep listening (e.g. skip a runtime /commands registration to
        // capture the /list value batch that follows it).
        explicit HttpCaptureServer(int response_status = 200, std::string only_path = {})
            : response_status_(response_status), only_path_(std::move(only_path))
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

            worker_ = std::thread([this] {
                while (!ServeOne())
                {
                }
            });
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
        // Block in select (not accept) so the destructor can wake us by setting stop_: a blocked
        // accept() is not portably interruptible by closing the socket from another thread, which
        // could deadlock the dtor's join() if no client ever connects. select() with a short
        // timeout re-checks stop_ each tick and unblocks reliably on both platforms.
        socket_t WaitForConnection()
        {
            while (!stop_.load(std::memory_order_acquire))
            {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(listen_, &readfds);

                timeval tv{};
                tv.tv_sec = 0;
                tv.tv_usec = 100000; // 100 ms

#if defined(_WIN32)
                const int nfds = 0; // ignored by winsock select; avoids a SOCKET->int truncation
#else
                const int nfds = listen_ + 1;
#endif
                const int ready = select(nfds, &readfds, nullptr, nullptr, &tv);
                if (ready < 0)
                    return INVALID_SOCKET; // socket torn down (dtor) or error
                if (ready == 0)
                    continue; // timeout — re-check stop_

                return accept(listen_, nullptr, nullptr);
            }
            return INVALID_SOCKET;
        }

        // Serve one connection. True when done: the request was captured, or the server is stopping.
        bool ServeOne()
        {
            socket_t conn = WaitForConnection();
            if (conn == INVALID_SOCKET)
                return true;
            bool captured = false;

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
                        // A malformed Content-Length must not throw out of the worker thread.
                        try
                        {
                            if (!cl.empty())
                            {
                                content_length = static_cast<size_t>(std::stoul(cl));
                                have_length = true;
                            }
                        }
                        catch (...)
                        {
                            have_length = false;
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

            // Parse defensively: a request line without the expected two spaces leaves the
            // captured fields empty rather than throwing std::out_of_range out of the thread.
            if (header_end != std::string::npos)
            {
                const std::string head = raw.substr(0, header_end);
                const size_t line_end = head.find("\r\n");
                const std::string request_line = head.substr(0, line_end == std::string::npos ? head.size() : line_end);
                const size_t sp1 = request_line.find(' ');
                const size_t sp2 = sp1 == std::string::npos ? std::string::npos : request_line.find(' ', sp1 + 1);

                if (sp1 != std::string::npos && sp2 != std::string::npos &&
                    (only_path_.empty() || request_line.substr(sp1 + 1, sp2 - sp1 - 1) == only_path_))
                {
                    request_.method = request_line.substr(0, sp1);
                    request_.path = request_line.substr(sp1 + 1, sp2 - sp1 - 1);
                    request_.headers = line_end == std::string::npos ? std::string{} : head.substr(line_end + 2);
                    request_.body = raw.substr(header_end + 4, have_length ? content_length : std::string::npos);
                    request_.received.store(true, std::memory_order_release);
                    captured = true;
                }
            }

            const std::string response =
                "HTTP/1.1 " + std::to_string(response_status_) + " X\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            send(conn, response.c_str(), static_cast<int>(response.size()), 0);
            closesocket(conn);
            return captured || only_path_.empty();
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
        std::string only_path_;
        std::atomic<bool> stop_{ false };
        std::thread worker_;
        CapturedRequest request_;
    };

    // Multi-request variant for the stop-drain tests (#1432): keeps accepting until destroyed and
    // records every request's path + body. Requests whose path starts with hang_path_prefix are
    // read and recorded but never answered — the connection is held open until the destructor, so
    // the client sees a server that accepted the request and hung. Everything else gets a 200.
    class HttpRecordingServer
    {
    public:
        struct Recorded
        {
            std::string path;
            std::string body;
            // The status the request was answered with; 0 while it has none (a request held open
            // by a hang is never answered). Set by HttpRecordingServer only.
            int status = 0;
        };

        // `reject_path_prefix`: requests to it are recorded and answered 400 Bad Request.
        explicit HttpRecordingServer(std::string hang_path_prefix = {}, std::string reject_path_prefix = {})
            : hang_path_prefix_(std::move(hang_path_prefix)), reject_path_prefix_(std::move(reject_path_prefix))
        {
#if defined(_WIN32)
            WSADATA wsa;
            WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
            listen_ = socket(AF_INET, SOCK_STREAM, 0);
            if (listen_ == INVALID_SOCKET)
                return; // Port() stays 0: the test fails on the first connect, not on UB in FD_SET

            sockaddr_in addr{};
            addr.sin_family = AF_INET;
            addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
            addr.sin_port = 0; // ephemeral

            int yes = 1;
            setsockopt(listen_, SOL_SOCKET, SO_REUSEADDR, reinterpret_cast<const char*>(&yes), sizeof(yes));
            // A failed bind/listen must not leave the accept thread selecting on a dead socket.
            if (bind(listen_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0 || listen(listen_, 16) != 0)
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

        ~HttpRecordingServer()
        {
            stop_.store(true, std::memory_order_release);
            if (worker_.joinable())
                worker_.join();
            for (const auto conn : hung_)
                closesocket(conn);
            if (listen_ != INVALID_SOCKET)
                closesocket(listen_);
#if defined(_WIN32)
            WSACleanup();
#endif
        }

        HttpRecordingServer(const HttpRecordingServer&) = delete;
        HttpRecordingServer& operator=(const HttpRecordingServer&) = delete;

        int Port() const { return port_; }

        std::vector<Recorded> Requests() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return requests_;
        }

        size_t CountPath(const std::string& path) const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            size_t count = 0;
            for (const auto& request : requests_)
                if (request.path == path)
                    ++count;
            return count;
        }

        // The next `count` requests to a path starting with `path_prefix` are recorded and answered
        // 502 Bad Gateway — a server restarting behind its reverse proxy (#1515).
        void FailNext(std::string path_prefix, int count)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            fail_path_prefix_ = std::move(path_prefix);
            fail_remaining_ = count;
        }

        // How EVERY request is answered from now on, whatever its path (the conformance driver's
        // set_sender_mode, #1751): with 200, with 503 Service Unavailable (a server that is down
        // behind its proxy), or not at all (recorded and held open like a hang_path_prefix
        // request). Read when a request has arrived, so set it while no request is on its way.
        enum class Answer
        {
            Ok,
            Unavailable,
            Never,
        };

        void SetAnswer(Answer answer)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            answer_ = answer;
        }

    private:
        friend class HttpKeepAliveServer; // reuses ReadRequest

        void AcceptLoop()
        {
            while (!stop_.load(std::memory_order_acquire))
            {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(listen_, &readfds);

                timeval tv{};
                tv.tv_sec = 0;
                tv.tv_usec = 50000; // 50 ms — re-check stop_ each tick

#if defined(_WIN32)
                const int nfds = 0;
#else
                const int nfds = listen_ + 1;
#endif
                const int ready = select(nfds, &readfds, nullptr, nullptr, &tv);
                if (ready < 0)
                {
#if !defined(_WIN32)
                    // A signal delivered to this thread makes select fail with EINTR; retrying keeps
                    // the fixture alive instead of killing the accept loop for the rest of the test.
                    if (errno == EINTR)
                        continue;
#endif
                    return; // socket torn down (dtor) or a genuine error
                }
                if (ready == 0)
                    continue;

                const socket_t conn = accept(listen_, nullptr, nullptr);
                if (conn == INVALID_SOCKET)
                    continue;

                Recorded recorded;
                if (!ReadRequest(conn, recorded))
                {
                    closesocket(conn);
                    continue;
                }

                const bool hang = !hang_path_prefix_.empty() && recorded.path.rfind(hang_path_prefix_, 0) == 0;
                const bool reject = !reject_path_prefix_.empty() && recorded.path.rfind(reject_path_prefix_, 0) == 0;
                bool fail = false;
                Answer answer = Answer::Ok;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    if (fail_remaining_ > 0 && recorded.path.rfind(fail_path_prefix_, 0) == 0)
                    {
                        --fail_remaining_;
                        fail = true;
                    }
                    answer = answer_;
                    // The answer is decided here, so it is recorded with the request: a reader that
                    // sees the request sees the status it gets (0: none, the request hangs).
                    recorded.status = (hang || answer == Answer::Never)
                                          ? 0
                                          : (fail ? 502 : (reject ? 400 : (answer == Answer::Unavailable ? 503 : 200)));
                    requests_.push_back(std::move(recorded));
                }

                if (hang || answer == Answer::Never)
                {
                    hung_.push_back(conn); // never answered; closed by the destructor
                    continue;
                }

                static const std::string ok = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                static const std::string bad = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                static const std::string gateway = "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                static const std::string unavailable =
                    "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                const std::string& response = fail ? gateway : (reject ? bad : (answer == Answer::Unavailable ? unavailable : ok));
                send(conn, response.c_str(), static_cast<int>(response.size()), 0);
                closesocket(conn);
            }
        }

        // Reads one request (headers + Content-Length body) with a blocking recv on the accept
        // thread, so connections are served strictly one at a time: fine for libcurl, which sends
        // the whole request immediately after connecting, but do not point a second concurrent
        // client at this fixture without giving each connection its own thread.
        static bool ReadRequest(socket_t conn, Recorded& out)
        {
            std::string raw;
            char buffer[4096];
            size_t header_end = std::string::npos;
            size_t content_length = 0;

            for (;;)
            {
                const int n = recv(conn, buffer, sizeof(buffer), 0);
                if (n <= 0)
                    return false;
                raw.append(buffer, static_cast<size_t>(n));

                if (header_end == std::string::npos)
                {
                    header_end = raw.find("\r\n\r\n");
                    if (header_end != std::string::npos)
                    {
                        std::string head = raw.substr(0, header_end);
                        for (auto& c : head)
                            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
                        const size_t pos = head.find("content-length:");
                        if (pos != std::string::npos)
                        {
                            try
                            {
                                content_length = static_cast<size_t>(std::stoul(head.substr(pos + 15)));
                            }
                            catch (...)
                            {
                                content_length = 0;
                            }
                        }
                    }
                }

                if (header_end != std::string::npos && raw.size() - (header_end + 4) >= content_length)
                    break;
            }

            const size_t sp1 = raw.find(' ');
            const size_t sp2 = sp1 == std::string::npos ? std::string::npos : raw.find(' ', sp1 + 1);
            if (sp2 == std::string::npos || sp2 > header_end)
                return false;

            out.path = raw.substr(sp1 + 1, sp2 - sp1 - 1);
            out.body = raw.substr(header_end + 4, content_length);
            return true;
        }

        std::string hang_path_prefix_;
        std::string reject_path_prefix_;
        socket_t listen_ = INVALID_SOCKET;
        int port_ = 0;
        std::atomic<bool> stop_{ false };
        std::thread worker_;
        std::vector<socket_t> hung_; // worker-thread only until the join in the destructor
        mutable std::mutex mutex_;
        std::vector<Recorded> requests_;
        std::string fail_path_prefix_; // guarded by mutex_
        int fail_remaining_ = 0;       // guarded by mutex_
        Answer answer_ = Answer::Ok;   // guarded by mutex_
    };

    // Keep-alive variant for the connection-reuse tests (#1653). Every request is answered 200
    // WITHOUT `Connection: close`, so an HTTP/1.1 client may keep the connection, and ACCEPTED
    // CONNECTIONS are counted separately from requests: the only way a test can tell "N requests
    // on one connection" from "N connections". Each connection is served on its own thread, so
    // concurrent clients are fine (unlike HttpRecordingServer).
    // `drop_after_response`: still answer without `Connection: close`, then close the socket. That
    // is a server or proxy dropping a kept connection without telling the client.
    // `hang_path_prefix`: requests to it are recorded and never answered (a hung server); the
    // connection stays open until the client gives up.
    class HttpKeepAliveServer
    {
    public:
        explicit HttpKeepAliveServer(bool drop_after_response = false, std::string hang_path_prefix = {})
            : drop_after_response_(drop_after_response), hang_path_prefix_(std::move(hang_path_prefix))
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
            if (bind(listen_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != 0 || listen(listen_, 16) != 0)
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

        ~HttpKeepAliveServer()
        {
            stop_.store(true, std::memory_order_release);
            if (worker_.joinable())
                worker_.join();
            for (auto& connection : connections_)
                if (connection.joinable())
                    connection.join();
            if (listen_ != INVALID_SOCKET)
                closesocket(listen_);
#if defined(_WIN32)
            WSACleanup();
#endif
        }

        HttpKeepAliveServer(const HttpKeepAliveServer&) = delete;
        HttpKeepAliveServer& operator=(const HttpKeepAliveServer&) = delete;

        int Port() const { return port_; }

        // Connections accepted so far (not the number currently open).
        int Connections() const { return accepted_.load(std::memory_order_acquire); }

        // Connections accepted and not yet closed by this server. Zero means every connection has
        // been closed on the server side: whatever the client wrote on them is recorded, and a
        // kept client connection has been sent its FIN.
        int OpenConnections() const { return open_.load(std::memory_order_acquire); }

        std::vector<HttpRecordingServer::Recorded> Requests() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return requests_;
        }

    private:
        // Wait until `sock` is readable; false when the server is stopping or the socket failed.
        bool WaitReadable(socket_t sock) const
        {
            while (!stop_.load(std::memory_order_acquire))
            {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(sock, &readfds);

                timeval tv{};
                tv.tv_sec = 0;
                tv.tv_usec = 50000; // 50 ms, then re-check stop_

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

        void AcceptLoop()
        {
            while (WaitReadable(listen_))
            {
                const socket_t conn = accept(listen_, nullptr, nullptr);
                if (conn == INVALID_SOCKET)
                    continue;

                open_.fetch_add(1, std::memory_order_release);
                accepted_.fetch_add(1, std::memory_order_release);
                connections_.emplace_back([this, conn] { ServeConnection(conn); });
            }
        }

        // Serve requests on one connection until the peer closes it or the server stops.
        void ServeConnection(socket_t conn)
        {
            while (WaitReadable(conn))
            {
                HttpRecordingServer::Recorded recorded;
                if (!HttpRecordingServer::ReadRequest(conn, recorded))
                    break; // peer closed (recv 0) or sent something unparseable

                const bool hang = !hang_path_prefix_.empty() && recorded.path.rfind(hang_path_prefix_, 0) == 0;
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    requests_.push_back(std::move(recorded));
                }
                if (hang)
                    continue; // never answered; the loop ends when the client closes the connection

                static const std::string ok = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
                send(conn, ok.c_str(), static_cast<int>(ok.size()), 0);
                if (drop_after_response_)
                    break;
            }
            closesocket(conn);
            open_.fetch_sub(1, std::memory_order_release);
        }

        bool drop_after_response_;
        std::string hang_path_prefix_;
        socket_t listen_ = INVALID_SOCKET;
        int port_ = 0;
        std::atomic<bool> stop_{ false };
        std::atomic<int> accepted_{ 0 };
        std::atomic<int> open_{ 0 };
        std::thread worker_;
        std::vector<std::thread> connections_; // accept-thread only until the join in the destructor
        mutable std::mutex mutex_;
        std::vector<HttpRecordingServer::Recorded> requests_;
    };
} // namespace hsm::test
