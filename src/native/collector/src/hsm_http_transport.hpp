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

        bool IsSuccess() const { return transport_ok && status_code >= 200 && status_code < 300; }
    };

    // One transport per collector. Each call uses its own libcurl easy handle, so concurrent calls
    // are safe. Cancel() aborts in-flight transfers (the CancelPendingRequests primitive); a fresh
    // ResetCancel() re-arms it for subsequent sends without tearing the transport down.
    class HttpTransport
    {
    public:
        HttpTransport(int64_t timeout_ms, bool verify_peer);
        ~HttpTransport();

        HttpTransport(const HttpTransport&) = delete;
        HttpTransport& operator=(const HttpTransport&) = delete;

        HttpResponse Post(const std::string& url, const std::string& json_body, const std::vector<HttpHeader>& headers);
        HttpResponse Get(const std::string& url, const std::vector<HttpHeader>& headers);

        void Cancel();
        void ResetCancel();

    private:
        int64_t timeout_ms_;
        bool verify_peer_;
        std::atomic<bool> cancelled_{ false };
    };
} // namespace hsm::http
