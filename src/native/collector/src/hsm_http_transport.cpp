#include "hsm_http_transport.hpp"

#include <curl/curl.h>

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

        // Returning non-zero aborts the transfer — the CancelPendingRequests primitive.
        int XferCallback(void* clientp, curl_off_t, curl_off_t, curl_off_t, curl_off_t)
        {
            return static_cast<std::atomic<bool>*>(clientp)->load() ? 1 : 0;
        }

        HttpResponse Perform(
            const std::string& url,
            const std::vector<HttpHeader>& headers,
            int64_t timeout_ms,
            bool verify_peer,
            std::atomic<bool>& cancelled,
            bool is_post,
            const std::string& body)
        {
            HttpResponse response;

            CURL* curl = curl_easy_init();
            if (curl == nullptr)
            {
                response.error = "curl_easy_init failed";
                return response;
            }

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
                    curl_easy_cleanup(curl);
                    response.error = "curl_slist_append failed";
                    return response;
                }
                header_list = appended;
            }

            curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
            curl_easy_setopt(curl, CURLOPT_HTTPHEADER, header_list);
            curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, WriteCallback);
            curl_easy_setopt(curl, CURLOPT_WRITEDATA, &response.body);
            curl_easy_setopt(curl, CURLOPT_TIMEOUT_MS, static_cast<long>(timeout_ms));
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, verify_peer ? 1L : 0L);
            curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, verify_peer ? 2L : 0L);
            curl_easy_setopt(curl, CURLOPT_NOPROGRESS, 0L);
            curl_easy_setopt(curl, CURLOPT_XFERINFOFUNCTION, XferCallback);
            curl_easy_setopt(curl, CURLOPT_XFERINFODATA, &cancelled);

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
            curl_easy_cleanup(curl);
            return response;
        }
    } // namespace

    HttpTransport::HttpTransport(int64_t timeout_ms, bool verify_peer)
        : timeout_ms_(timeout_ms), verify_peer_(verify_peer)
    {
        EnsureCurlGlobal();
    }

    HttpTransport::~HttpTransport() = default;

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
        return Perform(url, headers, timeout_ms_, verify_peer_, cancelled_, /*is_post=*/true, json_body);
    }

    HttpResponse HttpTransport::Get(const std::string& url, const std::vector<HttpHeader>& headers)
    {
        return Perform(url, headers, timeout_ms_, verify_peer_, cancelled_, /*is_post=*/false, std::string{});
    }
} // namespace hsm::http
