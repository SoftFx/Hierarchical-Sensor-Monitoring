// HSM Agent — background self-update checker (epic #1174).
//
// Flow per poll:
//   1. GET /api/agent/version  → { version, sha256, updateEnabled }
//   2. Compare with HSM_AGENT_VERSION (compile-time constant).
//   3. If server version > current AND updateEnabled: download exe with Key auth,
//      verify SHA-256, write to hsm-agent.new.exe, spawn --apply-update, RequestStop.
//
// HTTP: WinHTTP (built into Windows, no extra dep). TLS enforced; allow_untrusted_certificate
// mirrors the sensor-data setting so a self-signed server works in eval setups.
// SHA-256: Windows BCrypt (CNG).

#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winhttp.h>
#include <bcrypt.h>

#include "agent/update_checker.hpp"
#include "agent/paths.hpp"

#include <array>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

// LogLevel values mirror hsm::collector::LogLevel (info=2, error=4) without pulling in the
// collector header from this platform-specific translation unit.
static constexpr int kLogInfo = 2;
static constexpr int kLogWarn = 3;
static constexpr int kLogError = 4;

namespace hsm::agent
{
    // ---- Version comparison -----------------------------------------------------------------------

    struct SemVer
    {
        int major = 0, minor = 0, patch = 0;
        bool ok = false;
    };

    static SemVer ParseVersion(const std::string& s)
    {
        SemVer v;
        if (std::sscanf(s.c_str(), "%d.%d.%d", &v.major, &v.minor, &v.patch) == 3)
            v.ok = true;
        return v;
    }

    static bool IsNewer(const SemVer& candidate, const SemVer& current)
    {
        if (!candidate.ok || !current.ok)
            return false;
        if (candidate.major != current.major)
            return candidate.major > current.major;
        if (candidate.minor != current.minor)
            return candidate.minor > current.minor;
        return candidate.patch > current.patch;
    }

    // ---- SHA-256 via BCrypt -----------------------------------------------------------------------

    static std::string Sha256File(const std::wstring& path)
    {
        BCRYPT_ALG_HANDLE alg = nullptr;
        BCRYPT_HASH_HANDLE hash = nullptr;
        if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0)
            return {};

        std::string result;
        do
        {
            DWORD hash_len = 0, cb_data = 0;
            if (BCryptGetProperty(alg, BCRYPT_HASH_LENGTH, reinterpret_cast<PUCHAR>(&hash_len), sizeof(DWORD), &cb_data, 0) != 0)
                break;

            if (BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) != 0)
                break;

            // Read file in chunks and feed to the hash.
            HANDLE hfile = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (hfile == INVALID_HANDLE_VALUE)
                break;

            bool file_ok = true;
            std::array<BYTE, 65536> buf;
            DWORD read = 0;
            while (ReadFile(hfile, buf.data(), static_cast<DWORD>(buf.size()), &read, nullptr) && read > 0)
            {
                if (BCryptHashData(hash, buf.data(), read, 0) != 0)
                {
                    file_ok = false;
                    break;
                }
            }
            CloseHandle(hfile);
            if (!file_ok)
                break;

            std::vector<BYTE> digest(hash_len);
            if (BCryptFinishHash(hash, digest.data(), hash_len, 0) != 0)
                break;

            std::ostringstream ss;
            for (BYTE b : digest)
            {
                char hex[3];
                std::snprintf(hex, sizeof(hex), "%02x", b);
                ss << hex;
            }
            result = ss.str();
        } while (false);

        if (hash)
            BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(alg, 0);
        return result;
    }

    // ---- Tiny JSON reader — pull a single string field from a flat JSON object ------------------
    // The version manifest is simple: { "version": "0.5.1", "sha256": "...", "updateEnabled": true }

    static bool ExtractJsonString(const std::string& json, const std::string& key, std::string& out)
    {
        // Find "key": then the next quoted string.
        std::string needle = "\"" + key + "\"";
        auto pos = json.find(needle);
        if (pos == std::string::npos)
            return false;
        pos += needle.size();
        while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == ':'))
            ++pos;
        if (pos >= json.size() || json[pos] != '"')
            return false;
        ++pos;
        out.clear();
        while (pos < json.size() && json[pos] != '"')
            out += json[pos++];
        return true;
    }

    static bool ExtractJsonBool(const std::string& json, const std::string& key, bool& out)
    {
        std::string needle = "\"" + key + "\"";
        auto pos = json.find(needle);
        if (pos == std::string::npos)
            return false;
        pos += needle.size();
        while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == ':'))
            ++pos;
        if (json.compare(pos, 4, "true") == 0)
        {
            out = true;
            return true;
        }
        if (json.compare(pos, 5, "false") == 0)
        {
            out = false;
            return true;
        }
        return false;
    }

    // ---- WinHTTP helpers --------------------------------------------------------------------------

    static std::wstring Utf8ToWide(const std::string& s)
    {
        if (s.empty())
            return {};
        const int needed = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
        if (needed <= 0)
            return {};
        std::wstring w(static_cast<std::size_t>(needed - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, w.data(), needed);
        return w;
    }

    struct WinHttpSession
    {
        HINTERNET session = nullptr;
        HINTERNET connect = nullptr;

        explicit WinHttpSession(const std::string& host, int port, bool https, bool allow_untrusted)
        {
            session = WinHttpOpen(L"HSMAgent/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
            if (!session)
                return;

            connect = WinHttpConnect(session, Utf8ToWide(host).c_str(), static_cast<INTERNET_PORT>(port), 0);

            if (https && allow_untrusted)
            {
                DWORD flags = SECURITY_FLAG_IGNORE_UNKNOWN_CA | SECURITY_FLAG_IGNORE_CERT_DATE_INVALID |
                              SECURITY_FLAG_IGNORE_CERT_CN_INVALID | SECURITY_FLAG_IGNORE_CERT_WRONG_USAGE;
                WinHttpSetOption(session, WINHTTP_OPTION_SECURITY_FLAGS, &flags, sizeof(flags));
            }
        }

        ~WinHttpSession()
        {
            if (connect)
                WinHttpCloseHandle(connect);
            if (session)
                WinHttpCloseHandle(session);
        }

        bool Valid() const { return session != nullptr && connect != nullptr; }
    };

    // Do a GET request; return the body in `body_out` and optionally read a named response header.
    // Returns the HTTP status code, or 0 on transport failure.
    static int HttpGet(const WinHttpSession& sess, const std::wstring& path, bool https,
                       const std::wstring& extra_headers, std::vector<char>& body_out,
                       std::wstring* resp_header_name = nullptr, std::string* resp_header_out = nullptr)
    {
        if (!sess.Valid())
            return 0;

        const DWORD flags = https ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET req = WinHttpOpenRequest(sess.connect, L"GET", path.c_str(), nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (!req)
            return 0;

        if (!extra_headers.empty())
            WinHttpAddRequestHeaders(req, extra_headers.c_str(), static_cast<DWORD>(-1L), WINHTTP_ADDREQ_FLAG_ADD);

        bool sent = WinHttpSendRequest(req, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) &&
                    WinHttpReceiveResponse(req, nullptr);

        int status = 0;
        if (sent)
        {
            DWORD status_code = 0, cb = sizeof(DWORD);
            if (WinHttpQueryHeaders(req, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &status_code, &cb, WINHTTP_NO_HEADER_INDEX))
                status = static_cast<int>(status_code);

            if (status == 200)
            {
                // Read optional named response header.
                if (resp_header_name && resp_header_out)
                {
                    DWORD hdr_size = 0;
                    WinHttpQueryHeaders(req, WINHTTP_QUERY_CUSTOM, resp_header_name->c_str(), WINHTTP_NO_OUTPUT_BUFFER, &hdr_size, WINHTTP_NO_HEADER_INDEX);
                    if (GetLastError() == ERROR_INSUFFICIENT_BUFFER)
                    {
                        std::wstring wval(hdr_size / sizeof(wchar_t), L'\0');
                        if (WinHttpQueryHeaders(req, WINHTTP_QUERY_CUSTOM, resp_header_name->c_str(), wval.data(), &hdr_size, WINHTTP_NO_HEADER_INDEX))
                        {
                            // strip null terminator that WinHttp appends
                            while (!wval.empty() && wval.back() == L'\0')
                                wval.pop_back();
                            const int n = WideCharToMultiByte(CP_UTF8, 0, wval.c_str(), -1, nullptr, 0, nullptr, nullptr);
                            if (n > 0)
                            {
                                std::string s(static_cast<std::size_t>(n - 1), '\0');
                                WideCharToMultiByte(CP_UTF8, 0, wval.c_str(), -1, s.data(), n, nullptr, nullptr);
                                *resp_header_out = std::move(s);
                            }
                        }
                    }
                }

                // Stream the body.
                DWORD avail = 0;
                while (WinHttpQueryDataAvailable(req, &avail) && avail > 0)
                {
                    const std::size_t off = body_out.size();
                    body_out.resize(off + avail);
                    DWORD read = 0;
                    if (!WinHttpReadData(req, body_out.data() + off, avail, &read))
                    {
                        body_out.resize(off);
                        break;
                    }
                    body_out.resize(off + read);
                }
            }
        }

        WinHttpCloseHandle(req);
        return status;
    }

    // ---- Parse server URL into (scheme, host, port) ----------------------------------------------

    struct ServerUrl
    {
        bool https = true;
        std::string host;
        int port = 44330;
        bool ok = false;
    };

    static ServerUrl ParseServerUrl(const std::string& address, int config_port)
    {
        ServerUrl u;
        u.port = config_port;
        std::string addr = address;
        if (addr.compare(0, 8, "https://") == 0)
        {
            u.https = true;
            addr = addr.substr(8);
        }
        else if (addr.compare(0, 7, "http://") == 0)
        {
            u.https = false;
            addr = addr.substr(7);
        }
        else
        {
            u.https = true;
        }
        // strip trailing slash or path
        const auto slash = addr.find('/');
        if (slash != std::string::npos)
            addr = addr.substr(0, slash);
        // host:port
        const auto colon = addr.rfind(':');
        if (colon != std::string::npos)
        {
            try
            {
                u.port = std::stoi(addr.substr(colon + 1));
                addr = addr.substr(0, colon);
            }
            catch (...)
            {
            }
        }
        u.host = addr;
        u.ok = !u.host.empty();
        return u;
    }

    // ---- Spawn --apply-update detached process ---------------------------------------------------

    static bool SpawnApplyUpdate(const LogFn& log)
    {
        wchar_t exe_path[MAX_PATH] = {};
        if (!GetModuleFileNameW(nullptr, exe_path, MAX_PATH))
        {
            log(kLogError, "update: GetModuleFileName failed");
            return false;
        }

        std::wstring cmd_line = std::wstring(L"\"") + exe_path + L"\" --apply-update";
        STARTUPINFOW si = {};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi = {};
        if (!CreateProcessW(nullptr, cmd_line.data(), nullptr, nullptr, FALSE, DETACHED_PROCESS | CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
        {
            log(kLogError, "update: failed to spawn --apply-update");
            return false;
        }
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        log(kLogInfo, "update: --apply-update spawned");
        return true;
    }

    // ---- UpdateChecker implementation ------------------------------------------------------------

    static DWORD WINAPI UpdateThreadProc(LPVOID param)
    {
        static_cast<UpdateChecker*>(param)->Run();
        return 0;
    }

    UpdateChecker::UpdateChecker(const AgentConfig& config, LogFn log, std::function<void()> request_stop)
        : config_(config), log_(std::move(log)), request_stop_(std::move(request_stop))
    {
        stop_event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }

    UpdateChecker::~UpdateChecker()
    {
        Stop();
        if (stop_event_)
            CloseHandle(static_cast<HANDLE>(stop_event_));
    }

    void UpdateChecker::Start()
    {
        if (!config_.update_enabled)
            return;
        if (stop_event_ == nullptr)
            return;
        ResetEvent(static_cast<HANDLE>(stop_event_));
        thread_handle_ = CreateThread(nullptr, 0, UpdateThreadProc, this, 0, nullptr);
    }

    void UpdateChecker::Stop()
    {
        if (stop_event_)
            SetEvent(static_cast<HANDLE>(stop_event_));
        if (thread_handle_)
        {
            WaitForSingleObject(static_cast<HANDLE>(thread_handle_), 10000);
            CloseHandle(static_cast<HANDLE>(thread_handle_));
            thread_handle_ = nullptr;
        }
    }

    void UpdateChecker::Run()
    {
        if (!config_.update_enabled)
            return;

        // Jitter: wait 0–10 min on first boot so a fleet doesn't all check at once.
        const DWORD jitter_ms = static_cast<DWORD>(GetTickCount64() % 600000ULL);
        const DWORD period_ms = static_cast<DWORD>(config_.update_check_period_hours) * 3600000UL;

        HANDLE ev = static_cast<HANDLE>(stop_event_);

        // Initial jitter wait.
        if (WaitForSingleObject(ev, jitter_ms) == WAIT_OBJECT_0)
            return;

        while (true)
        {
            if (CheckAndUpdate())
                return; // update triggered — the service will be restarted by --apply-update

            if (WaitForSingleObject(ev, period_ms) == WAIT_OBJECT_0)
                return;
        }
    }

    bool UpdateChecker::CheckAndUpdate()
    {
        const ServerUrl url = ParseServerUrl(config_.server_address, config_.port);
        if (!url.ok)
        {
            log_(kLogWarn, "update: cannot parse server address");
            return false;
        }

        WinHttpSession sess(url.host, url.port, url.https, config_.allow_untrusted_certificate);
        if (!sess.Valid())
        {
            log_(kLogWarn, "update: WinHttp session failed");
            return false;
        }

        // Step 1: fetch version manifest.
        std::vector<char> manifest_body;
        const int status = HttpGet(sess, L"/api/agent/version", url.https, L"", manifest_body);
        if (status != 200)
        {
            log_(kLogWarn, "update: version manifest returned HTTP " + std::to_string(status));
            return false;
        }

        std::string manifest(manifest_body.begin(), manifest_body.end());

        std::string remote_version_str, remote_sha256;
        bool update_enabled = false;
        if (!ExtractJsonString(manifest, "version", remote_version_str) ||
            !ExtractJsonString(manifest, "sha256", remote_sha256) ||
            !ExtractJsonBool(manifest, "updateEnabled", update_enabled))
        {
            log_(kLogWarn, "update: cannot parse version manifest");
            return false;
        }

        if (!update_enabled)
        {
            log_(kLogInfo, "update: server has auto-update disabled");
            return false;
        }

        const SemVer remote = ParseVersion(remote_version_str);
        const SemVer current = ParseVersion(HSM_AGENT_VERSION);
        if (!IsNewer(remote, current))
        {
            log_(kLogInfo, "update: already up to date (" HSM_AGENT_VERSION ")");
            return false;
        }

        log_(kLogInfo, "update: new version available: " + remote_version_str + " (current: " HSM_AGENT_VERSION ")");

        // Step 2: download the exe.
        const std::wstring key_header = L"Key: " + Utf8ToWide(config_.access_key);
        std::vector<char> exe_body;
        std::wstring sha256_header_name = L"X-Agent-Sha256";
        std::string header_sha256;
        const int exe_status = HttpGet(sess, L"/api/agent/exe", url.https, key_header, exe_body, &sha256_header_name, &header_sha256);
        if (exe_status != 200 || exe_body.empty())
        {
            log_(kLogError, "update: exe download failed (HTTP " + std::to_string(exe_status) + ")");
            return false;
        }

        // Step 3: write to hsm-agent.new.exe.
        const std::wstring new_path = NewExePath();
        {
            HANDLE hf = CreateFileW(new_path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (hf == INVALID_HANDLE_VALUE)
            {
                log_(kLogError, "update: cannot write hsm-agent.new.exe");
                return false;
            }
            DWORD written = 0;
            WriteFile(hf, exe_body.data(), static_cast<DWORD>(exe_body.size()), &written, nullptr);
            CloseHandle(hf);
            if (written != static_cast<DWORD>(exe_body.size()))
            {
                log_(kLogError, "update: partial write to hsm-agent.new.exe");
                DeleteFileW(new_path.c_str());
                return false;
            }
        }

        // Step 4: verify SHA-256.
        const std::string actual_sha256 = Sha256File(new_path);
        if (actual_sha256.empty())
        {
            log_(kLogError, "update: SHA-256 computation failed");
            DeleteFileW(new_path.c_str());
            return false;
        }

        // Prefer manifest's sha256 (from /api/agent/version); fall back to response header.
        const std::string& expected = !remote_sha256.empty() ? remote_sha256 : header_sha256;
        if (actual_sha256 != expected)
        {
            log_(kLogError, "update: SHA-256 mismatch — expected " + expected + " got " + actual_sha256);
            DeleteFileW(new_path.c_str());
            return false;
        }

        log_(kLogInfo, "update: SHA-256 verified, staging update");

        // Step 5: spawn --apply-update, then request stop of the current service run.
        if (!SpawnApplyUpdate(log_))
        {
            DeleteFileW(new_path.c_str());
            return false;
        }

        log_(kLogInfo, "update: requesting service stop for restart");
        if (request_stop_)
            request_stop_();

        return true;
    }

} // namespace hsm::agent
