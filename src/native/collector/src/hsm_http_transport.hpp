#pragma once

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

// Internal C++ HTTP transport for the native collector (#1096 §12). NOT part of the C ABI — it
// sits behind the in-collector send path. libcurl is confined to hsm_http_transport.cpp so the
// rest of the core never sees <curl/curl.h> (and the -Werror gate never lints curl headers).
namespace hsm::http
{
    struct HttpHeader
    {
        std::string name;
        std::string value;
    };

    struct HttpResponse
    {
        // transport_ok = a response was received (any HTTP status). On a transport-level failure
        // (connect refused, timeout, cancelled) transport_ok is false and error carries the reason.
        bool transport_ok = false;
        long status_code = 0;
        std::string body;
        std::string error;
        // Response headers captured by the libcurl header callback. Names are lowercased.
        std::vector<HttpHeader> response_headers;

        bool IsSuccess() const { return transport_ok && status_code >= 200 && status_code < 300; }
    };

    // One transport per collector. Each call takes the kept libcurl easy handle (creating one when
    // it is absent or checked out by a concurrent call) and puts it back afterwards, so concurrent
    // calls are safe AND sequential calls reuse the handle's kept connection instead of paying a
    // TCP+TLS handshake per request (#1653), like the managed client's pooled keep-alive
    // connection. Only ONE idle handle is kept: a second one would strand an unused connection for
    // the life of the process once a single concurrent burst had created it. A handle is retired
    // kConnectionLifetime after its creation so DNS is resolved again (the managed
    // PooledConnectionLifetime bound, #1102-E4), and a connection idle for longer than
    // kConnectionIdleSeconds is not reused. A handle whose transfer failed is destroyed, never
    // kept: its connection and DNS answer are suspect.
    // Cancel() aborts in-flight transfers (the CancelPendingRequests primitive) AND every
    // send started while it is set, so the caller must ResetCancel() once the threads it wanted to
    // interrupt have quiesced — otherwise later sends (the stop drain, a restart's registration)
    // are aborted before they leave the process (#1432).
    class HttpTransport
    {
    public:
        // `connection_lifetime` is the age at which the kept handle is retired; a parameter so a
        // test can make it short enough to see the retirement happen.
        HttpTransport(int64_t timeout_ms, bool verify_peer, std::chrono::milliseconds connection_lifetime = kConnectionLifetime);
        ~HttpTransport();

        HttpTransport(const HttpTransport&) = delete;
        HttpTransport& operator=(const HttpTransport&) = delete;

        HttpResponse Post(const std::string& url, const std::string& json_body, const std::vector<HttpHeader>& headers);
        // Same POST with a per-request timeout instead of the transport default — the bounded stop
        // drain gives each send only what is left of its budget (#1432).
        HttpResponse Post(
            const std::string& url,
            const std::string& json_body,
            const std::vector<HttpHeader>& headers,
            int64_t timeout_ms);
        HttpResponse Get(const std::string& url, const std::vector<HttpHeader>& headers);

        void Cancel();
        void ResetCancel();

        // Upper bound on the age of a kept handle (and so of its connection and DNS answer);
        // mirrors HsmHttpsClient.ConnectionLifetime.
        static constexpr std::chrono::seconds kConnectionLifetime{ 300 };
        // A connection idle for longer than this is closed instead of reused: a NAT or firewall
        // may have dropped it without a FIN, and a send into it would hang until the request
        // timeout. Mirrors the managed handler's one-minute pooled-connection idle timeout.
        static constexpr long kConnectionIdleSeconds = 60;

    private:
        // A libcurl easy handle and when it was created. CURL* is kept opaque so <curl/curl.h>
        // stays confined to the .cpp.
        struct Handle
        {
            void* curl = nullptr;
            std::chrono::steady_clock::time_point created{};
        };

        HttpResponse Perform(
            const std::string& url,
            const std::vector<HttpHeader>& headers,
            int64_t timeout_ms,
            bool is_post,
            const std::string& body);

        Handle AcquireHandle();
        void ReleaseHandle(Handle handle, bool reusable);

        int64_t timeout_ms_;
        bool verify_peer_;
        std::chrono::milliseconds connection_lifetime_;
        std::atomic<bool> cancelled_{ false };
        std::mutex idle_mutex_;
        Handle idle_; // guarded by idle_mutex_; curl == nullptr when no handle is kept
    };
} // namespace hsm::http
