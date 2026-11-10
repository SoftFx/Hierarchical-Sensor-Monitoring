#include "hsm_http_transport.hpp"

#include <curl/curl.h>

#include <cctype>      // std::tolower in the header-name comparison
#include <string_view> // std::string_view used when splitting response headers

namespace hsm::http
{
    namespace
    {
        // libcurl needs one process-wide init before any easy handle is used. A function-local
        // static gives thread-safe once-only init. We deliberately DO NOT call
        // curl_global_cleanup(): it is not thread-safe and, run from a static destructor at
        // process exit, would race any still-live easy handles / worker threads (and TSan would
        // flag it). Leaking the one-time global allocation is the documented, safe choice for a
        // library that cannot prove all curl users have quiesced.
        void EnsureCurlGlobal()
        {
            struct CurlGlobal
            {
                CurlGlobal() { curl_global_init(CURL_GLOBAL_DEFAULT); }
            };
            static CurlGlobal global;
            (void)global;
        }

        size_t WriteCallback(char* ptr, size_t size, size_t nmemb, void* userdata)
        {
            const size_t total = size * nmemb;
            static_cast<std::string*>(userdata)->append(ptr, total);
            return total;
        }

        // Collect response headers. libcurl passes one line at a time including the status line and
        // the blank terminator. Lines that contain ':' are real headers; others are silently ignored.
        // Names are lowercased so callers can compare without a case-insensitive strcmp.
        size_t HeaderCallback(char* buffer, size_t size, size_t nitems, void* userdata)
        {
            const size_t total = size * nitems;
            const std::string_view line(buffer, total);
            const auto colon = line.find(':');
            if (colon == std::string_view::npos)
                return total;

            std::string name(line.substr(0, colon));
            for (auto& c : name)
                c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));

            std::string_view raw = line.substr(colon + 1);
            while (!raw.empty() && (raw.front() == ' ' || raw.front() == '\t'))
                raw.remove_prefix(1);
            while (!raw.empty() && (raw.back() == '\r' || raw.back() == '\n' || raw.back() == ' '))
                raw.remove_suffix(1);

            static_cast<std::vector<HttpHeader>*>(userdata)->push_back({ std::move(name), std::string(raw) });
            return total;
        }

        // Returning non-zero aborts the transfer — the CancelPendingRequests primitive.
        int XferCallback(void* clientp, curl_off_t, curl_off_t, curl_off_t, curl_off_t)
        {
            return static_cast<std::atomic<bool>*>(clientp)->load() ? 1 : 0;
        }
    } // namespace

    HttpTransport::Handle HttpTransport::AcquireHandle()
    {
        const auto now = std::chrono::steady_clock::now();

        Handle kept;
        {
            std::lock_guard<std::mutex> guard(idle_mutex_);
            kept = idle_;
            idle_ = Handle{};
        }

        if (kept.curl != nullptr)
        {
            if (now - kept.created < kConnectionLifetime)
                return kept;
            // Past its lifetime: closing the handle closes its connection and drops its DNS cache,
            // so the request below resolves the server name again.
            curl_easy_cleanup(static_cast<CURL*>(kept.curl));
        }

        return Handle{ curl_easy_init(), now };
    }

    void HttpTransport::ReleaseHandle(Handle handle, bool reusable)
    {
        if (reusable)
        {
            std::lock_guard<std::mutex> guard(idle_mutex_);
            if (idle_.curl == nullptr)
            {
                idle_ = handle;
                return;
            }
        }
        // Not reusable (failed transfer), or a concurrent call already put its handle back: closing
        // the handle closes its connection. Done outside the lock, it can block on a TLS shutdown.
        curl_easy_cleanup(static_cast<CURL*>(handle.curl));
    }

    HttpResponse HttpTransport::Perform(
        const std::string& url,
        const std::vector<HttpHeader>& headers,
        int64_t timeout_ms,
        bool is_post,
        const std::string& body)
    {
        HttpResponse response;

        // A send started while Cancel() is set must not leave the process (#1432). With a fresh
        // connection libcurl aborted it during the connect phase; on a kept connection the request
        // could be written before the first progress callback, so refuse it here instead.
        if (cancelled_.load())
        {
            response.error = curl_easy_strerror(CURLE_ABORTED_BY_CALLBACK);
            return response;
        }

        const Handle handle = AcquireHandle();
        CURL* curl = static_cast<CURL*>(handle.curl);
        if (curl == nullptr)
        {
            response.error = "curl_easy_init failed";
            return response;
        }
        // A pooled handle still carries the previous request's options (URL, headers, POST
        // body pointers into freed memory). Reset drops every option but keeps what makes the
        // handle worth pooling: live connections, the TLS session cache and the DNS cache.
        curl_easy_reset(curl);

        // curl_slist_append returns nullptr on allocation failure WITHOUT freeing the existing
        // list — overwriting header_list with that null would leak the earlier nodes and
        // silently drop already-appended headers. Append into a temp and only commit on success.
        curl_slist* header_list = nullptr;
        for (const auto& header : headers)
        {
            curl_slist* appended = curl_slist_append(header_list, (header.name + ": " + header.value).c_str());
            if (appended == nullptr)
            {
                curl_slist_free_all(header_list);
                ReleaseHandle(handle, /*reusable=*/true); // nothing was sent; the handle is intact
                response.error = "curl_slist_append failed";
                return response;
            }
            header_list = appended;
        }

        curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
        curl_easy_setopt(curl, CURLOPT_HTTPHEADER, header_list);
        curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteCallback);
        curl_easy_setopt(curl, CURLOPT_WRITEDATA, &response.body);
        curl_easy_setopt(curl, CURLOPT_HEADERFUNCTION, HeaderCallback);
        curl_easy_setopt(curl, CURLOPT_HEADERDATA, &response.response_headers);
        curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, static_cast<long>(timeout_ms));
        curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, verify_peer_ ? 1L : 0L);
        curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, verify_peer_ ? 2L : 0L);
        curl_easy_setopt(curl, CURLOPT_NOPROGRESS, 0L);
        curl_easy_setopt(curl, CURLOPT_XFERINFOFUNCTION, XferCallback);
        curl_easy_setopt(curl, CURLOPT_XFERINFODATA, &cancelled_);
        // Kept-connection hygiene (#1653). A connection idle past kConnectionIdleSeconds is not
        // reused (see the constant); TCP keep-alive probes, sent well inside that window, keep a
        // NAT mapping alive and let the stack notice a dead peer between sends. The lifetime bound
        // is enforced on the handle itself in AcquireHandle, independent of the libcurl version.
        curl_easy_setopt(curl, CURLOPT_MAXAGE_CONN, kConnectionIdleSeconds);
        curl_easy_setopt(curl, CURLOPT_TCP_KEEPALIVE, 1L);
        curl_easy_setopt(curl, CURLOPT_TCP_KEEPIDLE, 30L);
        curl_easy_setopt(curl, CURLOPT_TCP_KEEPINTVL, 15L);

        if (is_post)
        {
            curl_easy_setopt(curl, CURLOPT_POST, 1L);
            curl_easy_setopt(curl, CURLOPT_POSTFIELDS, body.c_str());
            curl_easy_setopt(curl, CURLOPT_POSTFIELDSIZE, static_cast<long>(body.size()));
        }

        const CURLcode rc = curl_easy_perform(curl);
        if (rc == CURLE_OK)
        {
            long code = 0;
            curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &code);
            response.transport_ok = true;
            response.status_code = code;
        }
        else
        {
            response.error = curl_easy_strerror(rc);
        }

        curl_slist_free_all(header_list);
        // The header list is freed above, so the handle must not be performed again without
        // the reset at the top of this function. After a transport-level failure (connect error,
        // timeout, cancel) the handle is destroyed rather than kept: libcurl closes the failed
        // connection itself, but the handle's DNS answer is suspect too. An HTTP error status is
        // a completed exchange on a healthy connection and keeps the handle.
        ReleaseHandle(handle, /*reusable=*/rc == CURLE_OK);
        return response;
    }

    HttpTransport::HttpTransport(int64_t timeout_ms, bool verify_peer)
        : timeout_ms_(timeout_ms), verify_peer_(verify_peer)
    {
        EnsureCurlGlobal();
    }

    HttpTransport::~HttpTransport()
    {
        // No request is in flight here: the collector joins its senders before dropping the
        // transport (the same contract cancelled_ already relied on).
        std::lock_guard<std::mutex> guard(idle_mutex_);
        if (idle_.curl != nullptr)
            curl_easy_cleanup(static_cast<CURL*>(idle_.curl));
    }

    void HttpTransport::Cancel()
    {
        cancelled_.store(true);
    }

    void HttpTransport::ResetCancel()
    {
        cancelled_.store(false);
    }

    HttpResponse HttpTransport::Post(const std::string& url, const std::string& json_body, const std::vector<HttpHeader>& headers)
    {
        return Perform(url, headers, timeout_ms_, /*is_post=*/true, json_body);
    }

    HttpResponse HttpTransport::Post(
        const std::string& url,
        const std::string& json_body,
        const std::vector<HttpHeader>& headers,
        int64_t timeout_ms)
    {
        // A zero/negative timeout means "no timeout" to libcurl, which is the opposite of what a
        // spent budget asks for — clamp to 1 ms so the request fails fast instead of hanging. The
        // caller (the stop drain) already refuses a spent budget, so the clamp is a guard, not a
        // routine path. Caveat for sub-second values: with libcurl's standard synchronous resolver
        // (no threaded resolver / c-ares) the DNS phase keeps whole-second resolution, so a build
        // configured that way can overshoot a sub-second budget by up to ~1 s on a name lookup.
        return Perform(url, headers, timeout_ms > 0 ? timeout_ms : 1, /*is_post=*/true, json_body);
    }

    HttpResponse HttpTransport::Get(const std::string& url, const std::vector<HttpHeader>& headers)
    {
        return Perform(url, headers, timeout_ms_, /*is_post=*/false, std::string{});
    }
} // namespace hsm::http
