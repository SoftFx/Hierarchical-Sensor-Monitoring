#pragma once

#include <atomic>
#include <cstdint>
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

    // One transport per collector. Each call uses its own libcurl easy handle, so concurrent calls
    // are safe. Cancel() aborts in-flight transfers (the CancelPendingRequests primitive) AND every
    // send started while it is set, so the caller must ResetCancel() once the threads it wanted to
    // interrupt have quiesced — otherwise later sends (the stop drain, a restart's registration)
    // are aborted before they leave the process (#1432).
    class HttpTransport
    {
    public:
        HttpTransport(int64_t timeout_ms, bool verify_peer);
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

    private:
        int64_t timeout_ms_;
        bool verify_peer_;
        std::atomic<bool> cancelled_{ false };
    };
} // namespace hsm::http
