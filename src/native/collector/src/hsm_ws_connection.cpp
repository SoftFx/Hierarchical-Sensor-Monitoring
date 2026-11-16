#include "hsm_ws_connection.hpp"

#include "hsm_curl_support.hpp"

#include <algorithm>
#include <cctype>
#include <cstring>
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

// The API used below is complete in the headers of libcurl 8.14.0 (see kMinCurlVersion). Against
// older headers the class is compiled as the stub at the end of this file: it reports
// "unsupported" and never opens, so the collector stays on HTTP.
#if LIBCURL_VERSION_NUM >= 0x080e00

namespace hsm::http
{
    // The preprocessor cannot read the constant, so the two spellings of the minimum are tied here.
    static_assert(WebSocketConnection::kMinCurlVersion == 0x080e00, "keep the #if above in step with kMinCurlVersion");

    namespace
    {
        constexpr const char* kCancelled = "cancelled";
        // 14 characters: fits every standard library's small-string buffer, so assigning it in a
        // catch handler cannot allocate and throw again.
        constexpr const char* kInternalError = "internal error";

        void SetInternalError(std::string& error) noexcept
        {
            try
            {
                error.assign(kInternalError);
            }
            catch (...)
            {
            }
        }

        bool HasLineBreak(const std::string& value)
        {
            return value.find('\r') != std::string::npos || value.find('\n') != std::string::npos;
        }

        bool StartsWithNoCase(std::string_view text, std::string_view lower_prefix)
        {
            if (text.size() < lower_prefix.size())
                return false;
            for (size_t i = 0; i < lower_prefix.size(); ++i)
            {
                if (static_cast<char>(std::tolower(static_cast<unsigned char>(text[i]))) != lower_prefix[i])
                    return false;
            }
            return true;
        }

        std::string_view Trim(std::string_view value)
        {
            const size_t first = value.find_first_not_of(" \t\r\n");
            if (first == std::string_view::npos)
                return {};
            const size_t last = value.find_last_not_of(" \t\r\n");
            return value.substr(first, last - first + 1);
        }

        int64_t RemainingMs(std::chrono::steady_clock::time_point deadline)
        {
            const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - std::chrono::steady_clock::now());
            return left.count() > 0 ? static_cast<int64_t>(left.count()) : 0;
        }

        // A control frame carries at most 125 bytes of payload (RFC 6455 5.5).
        constexpr size_t kMaxControlBytes = 125;

        // RFC 9110 5.6.2 token: what a header name may consist of. Keeps out the separators that
        // mean something in libcurl's header list syntax (':' and ';') along with everything else.
        bool IsHeaderName(const std::string& name)
        {
            constexpr std::string_view kPunctuation = "!#$%&'*+-.^_`|~";
            if (name.empty())
                return false;
            for (const char c : name)
            {
                if (std::isalnum(static_cast<unsigned char>(c)) == 0 && kPunctuation.find(c) == std::string_view::npos)
                    return false;
            }
            return true;
        }

        // Headers libcurl writes for the upgrade. One of the same name in CURLOPT_HTTPHEADER would
        // replace libcurl's, and the upgrade would no longer be the one libcurl validates.
        bool IsUpgradeHeader(const std::string& name)
        {
            const auto is = [&name](std::string_view lower) { return name.size() == lower.size() && StartsWithNoCase(name, lower); };
            return is("upgrade") || is("connection") || StartsWithNoCase(name, "sec-websocket-");
        }

        // What the upgrade response said, collected by OnUpgradeHeader.
        struct UpgradeHead
        {
            std::string subprotocol;
            int selections = 0; // Sec-WebSocket-Protocol lines in the final response head
        };

        // CURLOPT_HEADERFUNCTION: one call per header line of every response on the way to the
        // upgrade. libcurl validates the upgrade itself but does not look at the subprotocol, so
        // that one value is picked up here. Returning anything but the line length aborts the
        // transfer, which is what an exception turns into: nothing may escape into libcurl's C.
        size_t OnUpgradeHeader(char* buffer, size_t size, size_t nitems, void* userdata)
        {
            const size_t total = size * nitems;
            try
            {
                auto* head = static_cast<UpgradeHead*>(userdata);
                const std::string_view line(buffer, total);
                constexpr std::string_view kName = "sec-websocket-protocol:";

                if (StartsWithNoCase(line, "http/"))
                {
                    // A new response head: an interim 1xx can precede the 101.
                    head->subprotocol.clear();
                    head->selections = 0;
                }
                else if (StartsWithNoCase(line, kName))
                {
                    head->subprotocol = std::string(Trim(line.substr(kName.size())));
                    ++head->selections;
                }
                return total;
            }
            catch (...)
            {
                return total == 0 ? 1 : 0;
            }
        }

        // The address the handle connected to, for the log: "ip:port", "[ip]:port" for IPv6, empty
        // when libcurl did not get as far as a connection.
        std::string PeerAddress(CURL* curl)
        {
            char* ip = nullptr;
            long port = 0;
            if (curl_easy_getinfo(curl, CURLINFO_PRIMARY_IP, &ip) != CURLE_OK || ip == nullptr || *ip == '\0')
                return {};
            (void)curl_easy_getinfo(curl, CURLINFO_PRIMARY_PORT, &port);
            const std::string address(ip);
            const std::string host = address.find(':') != std::string::npos ? "[" + address + "]" : address;
            return port > 0 ? host + ":" + std::to_string(port) : host;
        }

        // CURLOPT_WRITEFUNCTION: the body of a refused upgrade (a 404 page) is dropped. Without
        // this libcurl would write it to stdout of the host process.
        size_t DiscardBody(char*, size_t size, size_t nitems, void*)
        {
            return size * nitems;
        }
    } // namespace

    bool WebSocketConnection::Supported(std::string* reason) noexcept
    {
        try
        {
            EnsureCurlGlobal();
            const curl_version_info_data* info = curl_version_info(CURLVERSION_NOW);
            if (info == nullptr)
            {
                if (reason != nullptr)
                    *reason = "libcurl does not report its version";
                return false;
            }
            if (info->version_num < kMinCurlVersion)
            {
                if (reason != nullptr)
                    *reason = std::string("libcurl ") + (info->version != nullptr ? info->version : "?") +
                              " is older than 8.14.0, the oldest whose WebSocket support is accepted";
                return false;
            }
            for (const char* const* protocol = info->protocols; protocol != nullptr && *protocol != nullptr; ++protocol)
            {
                if (std::strcmp(*protocol, "ws") == 0)
                    return true;
            }
            if (reason != nullptr)
                *reason = "this libcurl was built without WebSocket support";
            return false;
        }
        catch (...)
        {
            return false;
        }
    }

    WebSocketConnection::WebSocketConnection(bool verify_peer)
        : verify_peer_(verify_peer)
    {
        EnsureCurlGlobal();
    }

    WebSocketConnection::~WebSocketConnection()
    {
        Close(0);
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
        if (headers_ != nullptr)
        {
            curl_slist_free_all(static_cast<curl_slist*>(headers_));
            headers_ = nullptr;
        }
        detail_.clear(); // after the cleanup above: the handle pointed into it
        // swap, not clear: a large message must not pin its buffer for the life of the object.
        std::string().swap(message_);
        control_.clear();
    }

    // libcurl clears its error buffer only when a transfer starts. Before each later call the old
    // text is dropped, so a failure is never described with the words of an earlier one.
    void WebSocketConnection::ForgetDetail()
    {
        if (!detail_.empty())
            detail_[0] = '\0';
    }

    // libcurl's own description of the last failure on this handle when it gave one (it names the
    // protocol violation, the refused status, the TLS problem), else the generic text of the code.
    std::string WebSocketConnection::Describe(int curl_code) const
    {
        if (!detail_.empty() && detail_[0] != '\0')
            return std::string(detail_.data());
        return curl_easy_strerror(static_cast<CURLcode>(curl_code));
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

    // Writes one whole frame. libcurl encodes it; when the socket blocks part-way, curl_ws_send
    // reports how much of the payload it took and expects the rest of the SAME frame next, so the
    // loop below only ever continues a frame it started.
    bool WebSocketConnection::SendFrame(const char* data, size_t size, unsigned int flags, Deadline deadline, std::string& error)
    {
        CURL* curl = static_cast<CURL*>(curl_);
        size_t offset = 0;
        for (;;)
        {
            if (cancelled_.load())
            {
                error = kCancelled;
                return false;
            }

            size_t sent = 0;
            ForgetDetail();
            const CURLcode rc = curl_ws_send(curl, data + offset, size - offset, &sent, 0, flags);
            if (rc == CURLE_OK)
            {
                offset += (std::min)(sent, size - offset);
                if (offset >= size)
                    return true;
                // A partial write means the socket blocked: wait for it below.
            }
            else if (rc != CURLE_AGAIN)
            {
                error = Describe(rc);
                return false;
            }

            switch (WaitForSocket(/*for_read=*/false, deadline))
            {
            case Wait::Ready:
                break;
            case Wait::Timeout:
                error = "send timed out";
                return false;
            case Wait::Cancelled:
                error = kCancelled;
                return false;
            case Wait::Failed:
                error = "waiting on the connection failed";
                return false;
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
        closure_ = WebSocketClosure{};

        try
        {
            if (cancelled_.load())
            {
                result.error = kCancelled;
                return result;
            }
            if (!Supported(&result.error))
            {
                if (result.error.empty())
                    result.error = "libcurl cannot carry a WebSocket";
                return result;
            }

            if ((!StartsWithNoCase(url, "ws://") && !StartsWithNoCase(url, "wss://")) || HasLineBreak(url) ||
                url.find(' ') != std::string::npos)
            {
                result.error = "the URL must be a ws:// or wss:// URL";
                return result;
            }

            // A header that carries a line break would let its value smuggle extra header lines
            // (or a second request) into the upgrade. Refuse rather than sanitize.
            if (HasLineBreak(subprotocol))
            {
                result.error = "the subprotocol contains a line break";
                return result;
            }
            for (const auto& header : headers)
            {
                if (!IsHeaderName(header.name) || HasLineBreak(header.value))
                {
                    result.error = "a request header name or value is malformed";
                    return result;
                }
                if (IsUpgradeHeader(header.name))
                {
                    result.error = "a request header would replace one of the upgrade's own";
                    return result;
                }
            }

            CURL* curl = curl_easy_init();
            if (curl == nullptr)
            {
                result.error = "curl_easy_init failed";
                return result;
            }
            curl_ = curl;

            // The list is owned through headers_ from the first node on, so every exit below
            // frees it in Release().
            const auto add_header = [this](const std::string& line) {
                curl_slist* grown = curl_slist_append(static_cast<curl_slist*>(headers_), line.c_str());
                if (grown == nullptr)
                    return false;
                headers_ = grown;
                return true;
            };
            bool listed = subprotocol.empty() || add_header("Sec-WebSocket-Protocol: " + subprotocol);
            for (const auto& header : headers)
            {
                // "Name;" is libcurl's spelling of a header with an empty value; "Name:" removes it.
                listed = listed && add_header(header.value.empty() ? header.name + ";" : header.name + ": " + header.value);
            }
            if (!listed)
            {
                result.error = "out of memory building the upgrade request";
                Release();
                return result;
            }

            // At least 1 ms: libcurl reads 0 as "no timeout".
            const int64_t open_timeout_ms = std::min<int64_t>(std::max<int64_t>(timeout_ms, 1), kMaxTimeoutMs);

            UpgradeHead head;
            detail_.assign(CURL_ERROR_SIZE, '\0');

            curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
            // 2 = connect, send the WebSocket upgrade, verify the answer and stop. From then on
            // the handle is driven with curl_ws_send / curl_ws_recv.
            curl_easy_setopt(curl, CURLOPT_CONNECT_ONLY, 2L);
            // Pings are answered in Receive, at once. libcurl's automatic pong can sit in its
            // queue until the next send (see kMinCurlVersion).
            curl_easy_setopt(curl, CURLOPT_WS_OPTIONS, static_cast<long>(CURLWS_NOAUTOPONG));
            // Nothing but a WebSocket may come out of this handle, whatever the URL says.
            curl_easy_setopt(curl, CURLOPT_PROTOCOLS_STR, "ws,wss");
            // libcurl speaks the upgrade over HTTP/1.1 only and pins that itself. Stated here as
            // well so the session does not start depending on what a later libcurl negotiates.
            curl_easy_setopt(curl, CURLOPT_HTTP_VERSION, static_cast<long>(CURL_HTTP_VERSION_1_1));
            // Through an HTTP proxy the connection must be a CONNECT tunnel, or the frames would be
            // interpreted by the proxy.
            curl_easy_setopt(curl, CURLOPT_HTTPPROXYTUNNEL, 1L);
            curl_easy_setopt(curl, CURLOPT_HTTPHEADER, static_cast<curl_slist*>(headers_));
            curl_easy_setopt(curl, CURLOPT_HEADERFUNCTION, OnUpgradeHeader);
            curl_easy_setopt(curl, CURLOPT_HEADERDATA, &head);
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, DiscardBody);
            curl_easy_setopt(curl, CURLOPT_ERRORBUFFER, detail_.data());
            curl_easy_setopt(curl, CURLOPT_NOSIGNAL, 1L);
            curl_easy_setopt(curl, CURLOPT_CONNECTTIMEOUT_MS, static_cast<long>(open_timeout_ms));
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

            // `head` lives on this stack frame; the handle outlives it.
            curl_easy_setopt(curl, CURLOPT_HEADERFUNCTION, nullptr);
            curl_easy_setopt(curl, CURLOPT_HEADERDATA, nullptr);
            // The transfer timeout bounded the open above. It must not stay armed on a handle that
            // lives for hours: it would fail a healthy idle connection once it had elapsed.
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, 0L);

            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &result.status_code);
            result.peer = PeerAddress(curl);
            if (rc != CURLE_OK)
            {
                if (cancelled_.load())
                    result.error = kCancelled;
                else
                    result.error = Describe(rc);
                Release();
                return result;
            }

            ForgetDetail(); // the open succeeded: whatever libcurl noted on the way is history

            result.subprotocol = head.subprotocol;
            if (head.selections > 1 || head.subprotocol != subprotocol)
            {
                if (head.selections > 1)
                    result.error = "the server sent more than one subprotocol selection";
                else
                    result.error = subprotocol.empty() ? "the server selected a subprotocol that was not offered"
                                                       : "the server did not select the offered subprotocol";
                Close();
                return result;
            }

            result.ok = true;
        }
        catch (...)
        {
            SetInternalError(result.error);
            Release();
            result.ok = false;
        }
        return result;
    }

    bool WebSocketConnection::SendText(const std::string& text, int64_t timeout_ms, std::string& error)
    {
        try
        {
            if (curl_ == nullptr)
            {
                error = "not connected";
                return false;
            }
            if (SendFrame(text.data(), text.size(), CURLWS_TEXT, DeadlineIn(timeout_ms), error))
                return true;
            // The frame may be half-written: the connection cannot carry another one.
            Release();
            return false;
        }
        catch (...)
        {
            SetInternalError(error);
            Release();
            return false;
        }
    }

    WebSocketReceive WebSocketConnection::Receive(std::string& text, int64_t timeout_ms, std::string& error)
    {
        try
        {
            text.clear();
            if (curl_ == nullptr)
            {
                error = "not connected";
                return WebSocketReceive::Error;
            }

            CURL* curl = static_cast<CURL*>(curl_);
            const Deadline deadline = DeadlineIn(timeout_ms);
            for (bool first = true;; first = false)
            {
                if (cancelled_.load())
                {
                    error = kCancelled;
                    Release();
                    return WebSocketReceive::Error;
                }
                // Frames that complete no message (pings, fragments) must not hold the caller past
                // the deadline. What has arrived stays buffered for the next call.
                if (!first && RemainingMs(deadline) <= 0)
                    return WebSocketReceive::Timeout;

                char buffer[16384];
                size_t received = 0;
                const curl_ws_frame* meta = nullptr;
                // Always try the read first: with TLS, bytes can sit decrypted inside libcurl while
                // the socket itself is not readable, so waiting on the socket first could stall.
                ForgetDetail();
                const CURLcode rc = curl_ws_recv(curl, buffer, sizeof(buffer), &received, &meta);
                if (rc == CURLE_AGAIN)
                {
                    if (RemainingMs(deadline) <= 0)
                        return WebSocketReceive::Timeout;

                    const Wait waited = WaitForSocket(/*for_read=*/true, deadline);
                    if (waited == Wait::Cancelled || waited == Wait::Failed)
                    {
                        error = waited == Wait::Cancelled ? kCancelled : "waiting on the connection failed";
                        Release();
                        return WebSocketReceive::Error;
                    }
                    continue;
                }
                if (rc == CURLE_GOT_NOTHING)
                {
                    // libcurl read the end of the stream (recv returned 0) before any close frame:
                    // the TCP connection ended without the WebSocket close handshake.
                    closure_ = WebSocketClosure{};
                    Release();
                    return WebSocketReceive::Closed;
                }
                if (rc != CURLE_OK || meta == nullptr)
                {
                    // Includes every protocol violation libcurl detects in a frame.
                    error = rc != CURLE_OK ? Describe(rc) : "libcurl returned no frame information";
                    Release();
                    return WebSocketReceive::Error;
                }

                const unsigned int flags = static_cast<unsigned int>(meta->flags);
                if ((flags & CURLWS_PONG) != 0)
                    continue; // an unsolicited pong is a keep-alive from the peer, not a message
                if ((flags & (CURLWS_CLOSE | CURLWS_PING)) != 0)
                {
                    // libcurl hands a frame out as its bytes arrive, so even a control payload can
                    // come in pieces. It is acted on once, when it is whole.
                    if (control_.size() + received > kMaxControlBytes)
                    {
                        error = "control frame exceeds 125 bytes";
                        Release();
                        return WebSocketReceive::Error;
                    }
                    control_.append(buffer, received);
                    if (meta->bytesleft > 0)
                        continue;

                    std::string payload;
                    payload.swap(control_);
                    if ((flags & CURLWS_CLOSE) != 0)
                    {
                        // Complete the close handshake with one non-blocking attempt, echoing the
                        // peer's status code when it sent one.
                        size_t sent = 0;
                        (void)curl_ws_send(curl, payload.data(), payload.size() >= 2 ? 2 : 0, &sent, 0, CURLWS_CLOSE);
                        // RFC 6455 5.5.1: an optional 2-byte status code in network byte order,
                        // then an optional reason text.
                        closure_ = WebSocketClosure{};
                        closure_.close_frame = true;
                        if (payload.size() >= 2)
                        {
                            closure_.code = (static_cast<int>(static_cast<unsigned char>(payload[0])) << 8) |
                                            static_cast<int>(static_cast<unsigned char>(payload[1]));
                            // Only for the log: running out of memory here must not turn the
                            // close into an Error.
                            try
                            {
                                closure_.reason.assign(payload, 2, std::string::npos);
                            }
                            catch (...)
                            {
                            }
                        }
                        Release();
                        return WebSocketReceive::Closed;
                    }
                    // The pong is a complete frame: it gets its own short bound rather than the
                    // caller's deadline, which may already have passed.
                    if (!SendFrame(payload.data(), payload.size(), CURLWS_PONG, DeadlineIn(1000), error))
                    {
                        Release();
                        return WebSocketReceive::Error;
                    }
                    continue;
                }
                if ((flags & CURLWS_TEXT) == 0)
                {
                    error = (flags & CURLWS_BINARY) != 0 ? "binary messages are not supported" : "unexpected frame type";
                    Release();
                    return WebSocketReceive::Error;
                }

                // bytesleft is the rest of this frame, known from its header: an oversized frame is
                // refused on its first chunk, before the payload is read.
                const uint64_t pending = meta->bytesleft > 0 ? static_cast<uint64_t>(meta->bytesleft) : 0;
                if (static_cast<uint64_t>(message_.size()) + received + pending > kMaxMessageBytes)
                {
                    error = "message exceeds the size limit";
                    Release();
                    return WebSocketReceive::Error;
                }

                message_.append(buffer, received);
                // CURLWS_CONT: this frame is not the last fragment of its message.
                if (pending == 0 && (flags & CURLWS_CONT) == 0)
                {
                    text.swap(message_);
                    std::string().swap(message_);
                    return WebSocketReceive::Text;
                }
            }
        }
        catch (...)
        {
            SetInternalError(error);
            Release();
            return WebSocketReceive::Error;
        }
    }

    void WebSocketConnection::Close(int64_t linger_ms)
    {
        if (curl_ != nullptr)
        {
            try
            {
                CURL* curl = static_cast<CURL*>(curl_);
                // Status 1000 (normal closure), network byte order.
                const char normal_closure[2] = { static_cast<char>(0x03), static_cast<char>(0xE8) };

                if (linger_ms <= 0 || cancelled_.load())
                {
                    // One non-blocking attempt: this path must never wait.
                    size_t sent = 0;
                    (void)curl_ws_send(curl, normal_closure, sizeof(normal_closure), &sent, 0, CURLWS_CLOSE);
                }
                else
                {
                    // Closing a socket that still has unread inbound bytes makes the OS send a
                    // reset, which can discard the close frame just queued; the server then sees an
                    // aborted connection instead of a normal closure. So send the close frame, then
                    // read and drop what the peer sends until its own close frame (or the end of
                    // the stream) arrives, within the linger budget.
                    const Deadline deadline = DeadlineIn(std::min<int64_t>(linger_ms, kMaxLingerMs));
                    std::string ignored;
                    bool waiting = SendFrame(normal_closure, sizeof(normal_closure), CURLWS_CLOSE, deadline, ignored);
                    // The budget and a cancel are checked on every pass: a peer that keeps talking
                    // must not hold the stop past the linger.
                    while (waiting && !cancelled_.load() && RemainingMs(deadline) > 0)
                    {
                        char buffer[4096];
                        size_t received = 0;
                        const curl_ws_frame* meta = nullptr;
                        const CURLcode rc = curl_ws_recv(curl, buffer, sizeof(buffer), &received, &meta);
                        if (rc == CURLE_AGAIN)
                            waiting = WaitForSocket(/*for_read=*/true, deadline) == Wait::Ready;
                        else
                            waiting = rc == CURLE_OK && meta != nullptr && (static_cast<unsigned int>(meta->flags) & CURLWS_CLOSE) == 0;
                    }
                }
            }
            catch (...)
            {
                // Close runs from the destructor: nothing may escape it.
            }
        }
        Release();
    }
} // namespace hsm::http

#else // libcurl headers older than 8.14.0

namespace hsm::http
{
    namespace
    {
        constexpr const char* kTooOld = "built against a libcurl older than 8.14.0, the oldest whose WebSocket support is accepted";

        void Assign(std::string& target, const char* text) noexcept
        {
            try
            {
                target.assign(text);
            }
            catch (...)
            {
            }
        }
    } // namespace

    bool WebSocketConnection::Supported(std::string* reason) noexcept
    {
        if (reason != nullptr)
            Assign(*reason, kTooOld);
        return false;
    }

    WebSocketConnection::WebSocketConnection(bool verify_peer)
        : verify_peer_(verify_peer)
    {
        (void)verify_peer_;
    }

    WebSocketConnection::~WebSocketConnection() = default;

    void WebSocketConnection::Cancel()
    {
        cancelled_.store(true);
    }

    WebSocketOpenResult WebSocketConnection::Open(const std::string&, const std::vector<HttpHeader>&, const std::string&, int64_t)
    {
        WebSocketOpenResult result;
        Assign(result.error, kTooOld);
        return result;
    }

    bool WebSocketConnection::SendText(const std::string&, int64_t, std::string& error)
    {
        Assign(error, "not connected");
        return false;
    }

    WebSocketReceive WebSocketConnection::Receive(std::string& text, int64_t, std::string& error)
    {
        text.clear();
        Assign(error, "not connected");
        return WebSocketReceive::Error;
    }

    void WebSocketConnection::Close(int64_t)
    {
    }
} // namespace hsm::http

#endif
