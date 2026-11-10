#pragma once

// Shared by the translation units that talk to libcurl (hsm_http_transport.cpp,
// hsm_ws_connection.cpp). Include it only from those: it pulls in <curl/curl.h>, which the rest of
// the core must not see.

#include <curl/curl.h>

#include <atomic>

namespace hsm::http
{
    // libcurl needs one process-wide init before any easy handle is used. The function is inline,
    // so its function-local static is ONE object across every translation unit that includes this
    // header: one thread-safe, once-only curl_global_init for the whole library. (Two separate
    // once-guards would each call curl_global_init, and two first uses on different threads would
    // race on libcurl older than 7.84, where that call is not thread-safe.)
    //
    // We deliberately DO NOT call curl_global_cleanup(): it is not thread-safe and, run from a
    // static destructor at process exit, would race any still-live easy handles / worker threads
    // (and TSan would flag it). Leaking the one-time global allocation is the documented, safe
    // choice for a library that cannot prove all curl users have quiesced.
    inline void EnsureCurlGlobal()
    {
        struct CurlGlobal
        {
            CurlGlobal() { curl_global_init(CURL_GLOBAL_DEFAULT); }
        };
        static CurlGlobal global;
        (void)global;
    }

    // CURLOPT_XFERINFOFUNCTION callback over a std::atomic<bool>*: returning non-zero aborts the
    // transfer. The CancelPendingRequests primitive of both transports.
    inline int CancelXferCallback(void* clientp, curl_off_t, curl_off_t, curl_off_t, curl_off_t)
    {
        return static_cast<std::atomic<bool>*>(clientp)->load() ? 1 : 0;
    }
} // namespace hsm::http
