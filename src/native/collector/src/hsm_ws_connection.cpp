#include "hsm_ws_connection.hpp"

#include "hsm_curl_support.hpp"
#include "hsm_ws_codec.hpp"

#include <algorithm>
#include <array>
#include <cctype>
#include <string_view>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#else
#include <cerrno>
#include <poll.h>
#endif

namespace hsm::http
{
    namespace
    {
        constexpr const char* kCancelled = "cancelled";
        constexpr const char* kInternalError = "internal error (out of memory?)";

        std::string ToLower(std::string value)
        {
            for (auto& c : value)
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            return value;
        }

        std::string Trim(const std::string& value)
        {
            const size_t first = value.find_first_not_of(" \t\r\n");
            if (first == std::string::npos)
                return {};
            const size_t last = value.find_last_not_of(" \t\r\n");
            return value.substr(first, last - first + 1);
        }

        bool HasLineBreak(const std::string& value)
        {
            return value.find('\r') != std::string::npos || value.find('\n') != std::string::npos;
        }

        // Value of a header in a response head ("HTTP/1.1 101 ...\r\nName: value\r\n...\r\n\r\n").
        std::string HeaderValue(const std::string& head, const std::string& name_lower)
        {
            const std::string lower = ToLower(head);
            const size_t pos = lower.find("\r\n" + name_lower + ":");
            if (pos == std::string::npos)
                return {};
            const size_t start = pos + 2 + name_lower.size() + 1;
            const size_t end = head.find("\r\n", start);
            return Trim(head.substr(start, end == std::string::npos ? std::string::npos : end - start));
        }

        int64_t RemainingMs(std::chrono::steady_clock::time_point deadline)
        {
            const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - std::chrono::steady_clock::now());
            return left.count() > 0 ? static_cast<int64_t>(left.count()) : 0;
        }

        struct Target
        {
            std::string curl_url;     // http(s):// form of the ws(s):// URL, for libcurl to connect to
            std::string host_header;  // host[:port], port only when it is not the scheme default
            std::string request_path; // path[?query]
        };

        // Splits a ws:// or wss:// URL. libcurl's own URL parser is not used: a libcurl built
        // without WebSocket support refuses those schemes.
        bool ParseTarget(const std::string& url, Target& target, std::string& error)
        {
            const std::string lower = ToLower(url.substr(0, 6));
            bool secure = false;
            size_t authority_start = 0;
            if (lower.compare(0, 5, "ws://") == 0)
            {
                authority_start = 5;
            }
            else if (lower.compare(0, 6, "wss://") == 0)
            {
                secure = true;
                authority_start = 6;
            }
            else
            {
                error = "the URL must start with ws:// or wss://";
                return false;
            }

            const size_t path_start = url.find_first_of("/?#", authority_start);
            const std::string authority =
                url.substr(authority_start, path_start == std::string::npos ? std::string::npos : path_start - authority_start);
            std::string path = path_start == std::string::npos ? std::string{} : url.substr(path_start);
            const size_t fragment = path.find('#');
            if (fragment != std::string::npos)
                path.erase(fragment);
            if (path.empty() || path[0] != '/')
                path.insert(path.begin(), '/');

            if (authority.empty() || authority.find('@') != std::string::npos || HasLineBreak(authority) || HasLineBreak(path) ||
                authority.find(' ') != std::string::npos || path.find(' ') != std::string::npos)
            {
                error = "the URL has no usable host";
                return false;
            }

            // Drop a port that equals the scheme default from the Host header (RFC 9110 7.2). An
            // IPv6 literal keeps its brackets; its colons are not a port separator.
            std::string host_header = authority;
            const size_t bracket = authority.rfind(']');
            const size_t colon = authority.rfind(':');
            if (colon != std::string::npos && (bracket == std::string::npos || colon > bracket))
            {
                const std::string port = authority.substr(colon + 1);
                if (port == (secure ? "443" : "80"))
                    host_header = authority.substr(0, colon);
            }

            target.curl_url = std::string(secure ? "https://" : "http://") + authority + path;
            target.host_header = host_header;
            target.request_path = path;
            return true;
        }
    } // namespace

    WebSocketConnection::WebSocketConnection(bool verify_peer)
        : verify_peer_(verify_peer)
    {
        EnsureCurlGlobal();

        // One generator per connection, seeded once. std::random_device can throw where there is
        // no entropy source (a bare container, descriptor exhaustion); the masks then fall back to
        // a clock-and-address seed. Masking exists to stop a hostile PAGE from choosing the bytes a
        // caching proxy sees; this client chooses its own payloads, so an unpredictable-enough
        // stream that never crashes the host is the right trade.
        try
        {
            std::random_device device;
            std::seed_seq seed{ device(), device(), device(), device() };
            random_.seed(seed);
        }
        catch (...)
        {
            const auto ticks = static_cast<uint64_t>(std::chrono::steady_clock::now().time_since_epoch().count());
            const auto self = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(this));
            std::seed_seq seed{ static_cast<uint32_t>(ticks), static_cast<uint32_t>(ticks >> 32), static_cast<uint32_t>(self),
                static_cast<uint32_t>(self >> 32) };
            random_.seed(seed);
        }
    }

    WebSocketConnection::~WebSocketConnection()
    {
        Close();
    }

    void WebSocketConnection::Cancel()
    {
        cancelled_.store(true);
    }

    WebSocketConnection::Deadline WebSocketConnection::DeadlineIn(int64_t timeout_ms)
    {
        const int64_t clamped = std::min<int64_t>(std::max<int64_t>(timeout_ms, 0), kMaxTimeoutMs);
        return std::chrono::steady_clock::now() + std::chrono::milliseconds(clamped);
    }

    void WebSocketConnection::Release()
    {
        if (curl_ != nullptr)
        {
            curl_easy_cleanup(static_cast<CURL*>(curl_));
            curl_ = nullptr;
        }
        inbuf_.clear();
        inpos_ = 0;
        message_.clear();
        assembling_ = false;
    }

    std::string WebSocketConnection::MaskedFrame(uint8_t opcode, const std::string& payload)
    {
        const uint32_t word = static_cast<uint32_t>(random_());
        const std::array<uint8_t, 4> mask{ static_cast<uint8_t>(word & 0xFFu), static_cast<uint8_t>((word >> 8) & 0xFFu),
            static_cast<uint8_t>((word >> 16) & 0xFFu), static_cast<uint8_t>((word >> 24) & 0xFFu) };
        return ws::EncodeClientFrame(opcode, payload, mask);
    }

    // Waits until the connection's socket is readable/writable, the deadline passes or Cancel() is
    // called. Waits in slices so a cancel is noticed without a wake-up primitive (see the header).
    WebSocketConnection::Wait WebSocketConnection::WaitForSocket(bool for_read, Deadline deadline)
    {
        curl_socket_t sock = CURL_SOCKET_BAD;
        if (curl_easy_getinfo(static_cast<CURL*>(curl_), CURLINFO_ACTIVESOCKET, &sock) != CURLE_OK || sock == CURL_SOCKET_BAD)
            return Wait::Failed;

        for (;;)
        {
            if (cancelled_.load())
                return Wait::Cancelled;

            const int64_t left = RemainingMs(deadline);
            if (left <= 0)
                return Wait::Timeout;
            const int slice = static_cast<int>(std::min<int64_t>(left, kWaitSliceMs));

#if defined(_WIN32)
            // A winsock fd_set is a list of handles, not a bitmap indexed by descriptor value, so
            // select has no FD_SETSIZE hazard here.
            fd_set fds;
            FD_ZERO(&fds);
            FD_SET(sock, &fds);

            timeval tv{};
            tv.tv_sec = 0;
            tv.tv_usec = slice * 1000;

            const int ready = select(0, for_read ? &fds : nullptr, for_read ? nullptr : &fds, nullptr, &tv);
            if (ready < 0)
                return Wait::Failed;
#else
            // poll, not select: a POSIX fd_set cannot hold a descriptor >= FD_SETSIZE, and a host
            // process with many open files hands out such descriptors.
            pollfd descriptor{};
            descriptor.fd = sock;
            descriptor.events = static_cast<short>(for_read ? POLLIN : POLLOUT);

            const int ready = poll(&descriptor, 1, slice);
            if (ready < 0)
            {
                if (errno == EINTR)
                    continue;
                return Wait::Failed;
            }
#endif
            // Ready also covers an error or hang-up on the socket: the next curl call reports it.
            if (ready > 0)
                return Wait::Ready;
        }
    }

    WebSocketConnection::Io WebSocketConnection::SendAll(const std::string& data, Deadline deadline, std::string& error)
    {
        CURL* curl = static_cast<CURL*>(curl_);
        size_t offset = 0;
        while (offset < data.size())
        {
            if (cancelled_.load())
            {
                error = kCancelled;
                return Io::Error;
            }

            size_t sent = 0;
            const CURLcode rc = curl_easy_send(curl, data.data() + offset, data.size() - offset, &sent);
            if (rc == CURLE_OK)
            {
                offset += sent;
                continue;
            }
            if (rc != CURLE_AGAIN)
            {
                error = curl_easy_strerror(rc);
                return Io::Error;
            }

            switch (WaitForSocket(/*for_read=*/false, deadline))
            {
            case Wait::Ready:
                break;
            case Wait::Timeout:
                error = "send timed out";
                return Io::Timeout;
            case Wait::Cancelled:
                error = kCancelled;
                return Io::Error;
            case Wait::Failed:
                error = "waiting on the connection failed";
                return Io::Error;
            }
        }
        return Io::Ok;
    }

    // Appends whatever the connection has to inbuf_, waiting up to the deadline for the first byte.
    // With the deadline already passed it makes exactly one non-blocking read attempt.
    WebSocketConnection::Io WebSocketConnection::ReadMore(Deadline deadline, std::string& error)
    {
        // Drop the parsed prefix before growing the buffer, so it never holds more than what is
        // still unparsed plus one read.
        if (inpos_ > 0)
        {
            inbuf_.erase(0, inpos_);
            inpos_ = 0;
        }

        CURL* curl = static_cast<CURL*>(curl_);
        for (;;)
        {
            if (cancelled_.load())
            {
                error = kCancelled;
                return Io::Error;
            }

            char buffer[16384];
            size_t received = 0;
            // Always try the read first: with TLS, bytes can sit decrypted inside libcurl while the
            // socket itself is not readable, so waiting on the socket before reading could stall.
            const CURLcode rc = curl_easy_recv(curl, buffer, sizeof(buffer), &received);
            if (rc == CURLE_OK)
            {
                if (received == 0)
                    return Io::Closed; // orderly TCP close by the peer
                inbuf_.append(buffer, received);
                return Io::Ok;
            }
            if (rc != CURLE_AGAIN)
            {
                error = curl_easy_strerror(rc);
                return Io::Error;
            }
            if (RemainingMs(deadline) <= 0)
                return Io::Timeout;

            switch (WaitForSocket(/*for_read=*/true, deadline))
            {
            case Wait::Ready:
            case Wait::Timeout:
                break; // read once more; a passed deadline is reported above after that read
            case Wait::Cancelled:
                error = kCancelled;
                return Io::Error;
            case Wait::Failed:
                error = "waiting on the connection failed";
                return Io::Error;
            }
        }
    }

    WebSocketOpenResult WebSocketConnection::Open(
        const std::string& url,
        const std::vector<HttpHeader>& headers,
        const std::string& subprotocol,
        int64_t timeout_ms)
    {
        WebSocketOpenResult result;
        Close();

        try
        {
            if (cancelled_.load())
            {
                result.error = kCancelled;
                return result;
            }

            Target target;
            if (!ParseTarget(url, target, result.error))
                return result;

            // A header that carries a line break would let its value smuggle extra header lines
            // (or a second request) into the upgrade. Refuse rather than sanitize.
            if (HasLineBreak(subprotocol))
            {
                result.error = "the subprotocol contains a line break";
                return result;
            }
            for (const auto& header : headers)
            {
                if (header.name.empty() || HasLineBreak(header.name) || HasLineBreak(header.value) ||
                    header.name.find(':') != std::string::npos)
                {
                    result.error = "a request header name or value is malformed";
                    return result;
                }
            }

            const int64_t open_timeout_ms = std::min<int64_t>(std::max<int64_t>(timeout_ms, 1), kMaxTimeoutMs);
            const Deadline deadline = DeadlineIn(open_timeout_ms);

            CURL* curl = curl_easy_init();
            if (curl == nullptr)
            {
                result.error = "curl_easy_init failed";
                return result;
            }
            curl_ = curl;

            curl_easy_setopt(curl, CURLOPT_URL, target.curl_url.c_str());
            // 1 = connect (TCP, TLS, proxy tunnel) and stop: no HTTP request is sent by libcurl.
            // The upgrade request and everything after it go through curl_easy_send / _recv.
            curl_easy_setopt(curl, CURLOPT_CONNECT_ONLY, 1L);
            // The upgrade below is written by hand as HTTP/1.1. Without this pin a libcurl built
            // with HTTP/2 offers `h2` in the TLS ALPN, the server selects it, and the hand-written
            // request lands on a connection the server reads as HTTP/2. Pinning 1.1 makes libcurl
            // offer only `http/1.1`.
            curl_easy_setopt(curl, CURLOPT_HTTP_VERSION, static_cast<long>(CURL_HTTP_VERSION_1_1));
            // Through an HTTP proxy the connection must be a CONNECT tunnel, or the frames would be
            // interpreted by the proxy.
            curl_easy_setopt(curl, CURLOPT_HTTPPROXYTUNNEL, 1L);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, static_cast<long>(open_timeout_ms));
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, verify_peer_ ? 1L : 0L);
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, verify_peer_ ? 2L : 0L);
            curl_easy_setopt(curl, CURLOPT_NOPROGRESS, 0L);
            curl_easy_setopt(curl, CURLOPT_XFERINFOFUNCTION, CancelXferCallback);
            curl_easy_setopt(curl, CURLOPT_XFERINFODATA, &cancelled_);
            curl_easy_setopt(curl, CURLOPT_TCP_KEEPALIVE, 1L);
            curl_easy_setopt(curl, CURLOPT_TCP_KEEPIDLE, 30L);
            curl_easy_setopt(curl, CURLOPT_TCP_KEEPINTVL, 15L);

            const CURLcode rc = curl_easy_perform(curl);
            if (rc != CURLE_OK)
            {
                result.error = curl_easy_strerror(rc);
                Release();
                return result;
            }

            std::array<uint8_t, 16> key_bytes{};
            for (size_t i = 0; i < key_bytes.size(); i += 4)
            {
                const uint32_t word = static_cast<uint32_t>(random_());
                for (size_t j = 0; j < 4; ++j)
                    key_bytes[i + j] = static_cast<uint8_t>((word >> (8 * j)) & 0xFFu);
            }
            const std::string key = ws::Base64(key_bytes.data(), key_bytes.size());

            std::string request = "GET " + target.request_path + " HTTP/1.1\r\n";
            request += "Host: " + target.host_header + "\r\n";
            request += "Upgrade: websocket\r\n";
            request += "Connection: Upgrade\r\n";
            request += "Sec-WebSocket-Key: " + key + "\r\n";
            request += "Sec-WebSocket-Version: 13\r\n";
            if (!subprotocol.empty())
                request += "Sec-WebSocket-Protocol: " + subprotocol + "\r\n";
            for (const auto& header : headers)
                request += header.name + ": " + header.value + "\r\n";
            request += "\r\n";

            if (SendAll(request, deadline, result.error) != Io::Ok)
            {
                Release();
                return result;
            }

            size_t head_end = std::string::npos;
            while ((head_end = inbuf_.find("\r\n\r\n")) == std::string::npos)
            {
                if (inbuf_.size() > kMaxHandshakeBytes)
                {
                    result.error = "the upgrade response is too large";
                    Release();
                    return result;
                }
                const Io io = ReadMore(deadline, result.error);
                if (io != Io::Ok)
                {
                    if (io == Io::Timeout)
                        result.error = "the upgrade timed out";
                    else if (io == Io::Closed)
                        result.error = "the server closed the connection during the upgrade";
                    Release();
                    return result;
                }
            }

            const std::string head = inbuf_.substr(0, head_end + 4);
            inpos_ = head_end + 4; // anything after the head is already frame data

            // Status line: "HTTP/1.1 101 Switching Protocols".
            const size_t space = head.find(' ');
            if (head.compare(0, 5, "HTTP/") != 0 || space == std::string::npos || head.size() < space + 4)
            {
                result.error = "the upgrade response is not HTTP";
                Release();
                return result;
            }
            long status = 0;
            for (size_t i = space + 1; i < space + 4; ++i)
            {
                if (head[i] < '0' || head[i] > '9')
                {
                    result.error = "the upgrade response has no status code";
                    Release();
                    return result;
                }
                status = status * 10 + (head[i] - '0');
            }
            result.status_code = status;
            result.subprotocol = HeaderValue(head, "sec-websocket-protocol");

            if (status != 101)
            {
                result.error = "the server refused the upgrade with HTTP " + std::to_string(status);
                Release();
                return result;
            }
            if (ToLower(HeaderValue(head, "upgrade")) != "websocket")
            {
                result.error = "the upgrade response does not switch to websocket";
                Release();
                return result;
            }
            // Proof that the peer understood this handshake and is not replaying a cached response.
            if (HeaderValue(head, "sec-websocket-accept") != ws::AcceptKey(key))
            {
                result.error = "the upgrade response carries a wrong Sec-WebSocket-Accept";
                Release();
                return result;
            }
            if (!subprotocol.empty() && result.subprotocol != subprotocol)
            {
                result.error = "the server did not select subprotocol '" + subprotocol + "'";
                Close();
                return result;
            }

            result.ok = true;
            return result;
        }
        catch (...)
        {
            Release();
            result.ok = false;
            result.error = kInternalError;
            return result;
        }
    }

    bool WebSocketConnection::SendText(const std::string& text, int64_t timeout_ms, std::string& error)
    {
        try
        {
            if (curl_ == nullptr)
            {
                error = "the connection is not open";
                return false;
            }
            if (SendAll(MaskedFrame(ws::kText, text), DeadlineIn(timeout_ms), error) == Io::Ok)
                return true;
        }
        catch (...)
        {
            error = kInternalError;
        }
        Release(); // a frame may be half-written: the stream is no longer in a known state
        return false;
    }

    WebSocketReceive WebSocketConnection::Receive(std::string& text, int64_t timeout_ms, std::string& error)
    {
        try
        {
            text.clear();
            if (curl_ == nullptr)
            {
                error = "the connection is not open";
                return WebSocketReceive::Error;
            }

            const Deadline deadline = DeadlineIn(timeout_ms);
            // Once the deadline has passed, one more read is allowed and then the call returns:
            // a peer that keeps the socket busy (a stream of pings, say) cannot hold Receive
            // beyond its timeout by more than one read's worth of frames.
            bool read_after_deadline = false;

            for (;;)
            {
                ws::Frame frame;
                size_t consumed = 0;
                const ws::ParseStatus status =
                    ws::ParseServerFrame(std::string_view(inbuf_).substr(inpos_), kMaxMessageBytes, frame, consumed, error);

                if (status == ws::ParseStatus::ProtocolError)
                {
                    Release();
                    return WebSocketReceive::Error;
                }

                if (status == ws::ParseStatus::NeedMore)
                {
                    if (RemainingMs(deadline) <= 0)
                    {
                        if (read_after_deadline)
                            return WebSocketReceive::Timeout;
                        read_after_deadline = true;
                    }

                    const Io io = ReadMore(deadline, error);
                    if (io == Io::Ok)
                        continue;
                    if (io == Io::Timeout)
                        return WebSocketReceive::Timeout;
                    Release();
                    return io == Io::Closed ? WebSocketReceive::Closed : WebSocketReceive::Error;
                }

                inpos_ += consumed;

                switch (frame.opcode)
                {
                case ws::kClose:
                {
                    // Complete the close handshake by echoing the status code, when there is one
                    // (a close payload is either empty or starts with a two-byte code). Best effort
                    // and bounded: the peer is leaving either way.
                    std::string ignored;
                    const std::string code = frame.payload.size() >= 2 ? frame.payload.substr(0, 2) : std::string{};
                    (void)SendAll(MaskedFrame(ws::kClose, code), DeadlineIn(200), ignored);
                    Release();
                    return WebSocketReceive::Closed;
                }
                case ws::kPing:
                    if (SendAll(MaskedFrame(ws::kPong, frame.payload), DeadlineIn(1000), error) != Io::Ok)
                    {
                        Release();
                        return WebSocketReceive::Error;
                    }
                    continue;
                case ws::kPong:
                    continue;
                case ws::kText:
                    if (!assembling_)
                        break;
                    error = "a new message started inside a fragmented one";
                    Release();
                    return WebSocketReceive::Error;
                case ws::kContinuation:
                    if (assembling_)
                        break;
                    error = "a continuation frame without a message to continue";
                    Release();
                    return WebSocketReceive::Error;
                case ws::kBinary:
                    error = "binary messages are not supported";
                    Release();
                    return WebSocketReceive::Error;
                default:
                    error = "unknown opcode";
                    Release();
                    return WebSocketReceive::Error;
                }

                if (message_.size() + frame.payload.size() > kMaxMessageBytes)
                {
                    error = "message exceeds the size limit";
                    Release();
                    return WebSocketReceive::Error;
                }

                if (frame.fin && !assembling_)
                {
                    text = std::move(frame.payload); // the common case: one frame, no extra copy
                    return WebSocketReceive::Text;
                }

                message_ += frame.payload;
                assembling_ = !frame.fin;
                if (frame.fin)
                {
                    text.swap(message_);
                    message_.clear();
                    return WebSocketReceive::Text;
                }
            }
        }
        catch (...)
        {
            error = kInternalError;
            Release();
            return WebSocketReceive::Error;
        }
    }

    void WebSocketConnection::Close()
    {
        if (curl_ != nullptr)
        {
            try
            {
                // Status 1000 (normal closure). One non-blocking attempt: Close must never wait,
                // and a peer that cannot take a few bytes right now is not owed a goodbye.
                std::string payload;
                payload.push_back(static_cast<char>(0x03));
                payload.push_back(static_cast<char>(0xE8));
                const std::string frame = MaskedFrame(ws::kClose, payload);
                size_t sent = 0;
                (void)curl_easy_send(static_cast<CURL*>(curl_), frame.data(), frame.size(), &sent);
            }
            catch (...)
            {
                // Close runs from the destructor: nothing may escape it.
            }
        }
        Release();
    }
} // namespace hsm::http
