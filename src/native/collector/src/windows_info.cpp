#include "windows_info.hpp"

#ifdef _WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <cstdio>
#include <cwchar>

namespace hsm::collector
{
    namespace
    {
        const wchar_t* kCurrentVersionKey = L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";

        bool RegReadDword(const wchar_t* subkey, const wchar_t* name, DWORD& out)
        {
            DWORD value = 0;
            DWORD size = sizeof(value);
            return RegGetValueW(HKEY_LOCAL_MACHINE, subkey, name, RRF_RT_REG_DWORD, nullptr, &value, &size) == ERROR_SUCCESS
                       ? (out = value, true)
                       : false;
        }

        std::string RegReadStringUtf8(const wchar_t* subkey, const wchar_t* name)
        {
            wchar_t buffer[512];
            DWORD size = sizeof(buffer);
            if (RegGetValueW(HKEY_LOCAL_MACHINE, subkey, name, RRF_RT_REG_SZ, nullptr, buffer, &size) != ERROR_SUCCESS)
                return std::string{};
            const int needed = WideCharToMultiByte(CP_UTF8, 0, buffer, -1, nullptr, 0, nullptr, nullptr);
            if (needed <= 1)
                return std::string{};
            std::string out(static_cast<std::size_t>(needed - 1), '\0');
            WideCharToMultiByte(CP_UTF8, 0, buffer, -1, out.data(), needed, nullptr, nullptr);
            return out;
        }
    } // namespace

    WindowsInfoSample ReadWindowsInfo()
    {
        WindowsInfoSample s;

        // Last restart = uptime. GetTickCount64 is monotonic ms since boot; .NET ticks are 100 ns.
        s.uptime_ticks = static_cast<std::int64_t>(GetTickCount64()) * 10000;
        s.has_uptime = true;

        // Install date = now - InstallDate. Registry "InstallDate" is a REG_DWORD Unix epoch (seconds).
        DWORD install_unix = 0;
        if (RegReadDword(kCurrentVersionKey, L"InstallDate", install_unix) && install_unix > 0)
        {
            FILETIME ft;
            GetSystemTimeAsFileTime(&ft);
            ULARGE_INTEGER now100;
            now100.LowPart = ft.dwLowDateTime;
            now100.HighPart = ft.dwHighDateTime;
            // FILETIME is 100-ns since 1601-01-01; Unix epoch offset is 116444736000000000 (100-ns units).
            const std::uint64_t now_unix = (now100.QuadPart - 116444736000000000ULL) / 10000000ULL;
            if (now_unix > install_unix)
            {
                s.install_age_ticks = static_cast<std::int64_t>(now_unix - install_unix) * 10000000LL; // s -> 100 ns
                s.has_install_age = true;
            }
        }

        // Version & patch from CurrentVersion. Major/Minor/UBR are DWORDs; CurrentBuildNumber is a string.
        DWORD major = 0, minor = 0, ubr = 0;
        if (RegReadDword(kCurrentVersionKey, L"CurrentMajorVersionNumber", major))
        {
            RegReadDword(kCurrentVersionKey, L"CurrentMinorVersionNumber", minor);
            RegReadDword(kCurrentVersionKey, L"UBR", ubr);

            int build = 0;
            const std::string build_str = RegReadStringUtf8(kCurrentVersionKey, L"CurrentBuildNumber");
            for (char c : build_str)
            {
                if (c < '0' || c > '9')
                    break;
                build = build * 10 + (c - '0');
            }

            s.ver_major = static_cast<std::int32_t>(major);
            s.ver_minor = static_cast<std::int32_t>(minor);
            s.ver_build = build;
            s.ver_ubr = static_cast<std::int32_t>(ubr);

            const std::string product = RegReadStringUtf8(kCurrentVersionKey, L"ProductName");
            const std::string display = RegReadStringUtf8(kCurrentVersionKey, L"DisplayVersion");
            s.version_comment = product + " " + display + " (" + std::to_string(s.ver_major) + "." +
                                std::to_string(s.ver_minor) + "." + std::to_string(s.ver_build) + ")";
            s.has_version = true;
        }

        // Last update = now - last successful Windows Update. The managed sensor uses WMI QFE
        // (InstalledOn); to stay COM-free, read the Automatic-Update last-success timestamp from the
        // registry ("yyyy-MM-dd HH:mm:ss", UTC). Absent key (AU never ran) -> leave empty.
        const std::string last_success = RegReadStringUtf8(
            L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\Results\\Install",
            L"LastSuccessTime");
        if (!last_success.empty())
        {
            int y = 0, mo = 0, d = 0, h = 0, mi = 0, se = 0;
            if (sscanf_s(last_success.c_str(), "%d-%d-%d %d:%d:%d", &y, &mo, &d, &h, &mi, &se) == 6)
            {
                SYSTEMTIME st{};
                st.wYear = static_cast<WORD>(y);
                st.wMonth = static_cast<WORD>(mo);
                st.wDay = static_cast<WORD>(d);
                st.wHour = static_cast<WORD>(h);
                st.wMinute = static_cast<WORD>(mi);
                st.wSecond = static_cast<WORD>(se);

                FILETIME update_ft;
                if (SystemTimeToFileTime(&st, &update_ft)) // SYSTEMTIME is interpreted as UTC
                {
                    ULARGE_INTEGER update100;
                    update100.LowPart = update_ft.dwLowDateTime;
                    update100.HighPart = update_ft.dwHighDateTime;

                    FILETIME now_ft;
                    GetSystemTimeAsFileTime(&now_ft);
                    ULARGE_INTEGER now100b;
                    now100b.LowPart = now_ft.dwLowDateTime;
                    now100b.HighPart = now_ft.dwHighDateTime;

                    if (now100b.QuadPart > update100.QuadPart)
                    {
                        s.last_update_age_ticks = static_cast<std::int64_t>(now100b.QuadPart - update100.QuadPart);
                        s.has_last_update_age = true;
                    }
                }
            }
        }

        return s;
    }

    namespace
    {
        // Read new Error/Warning records from one classic event log since *cursor (next unread
        // RecordNumber). On the seeding pass, only advance the cursor to the newest record (no
        // backfill), matching the managed event-driven sensor which reports only entries written
        // while it runs. Message is a best-effort join of the insertion strings (the fully-rendered
        // text needs the source's message DLL, intentionally skipped here).
        void PollOneLog(const wchar_t* log_name, std::uint32_t& cursor, bool seeded,
                        EventLogKind error_kind, EventLogKind warning_kind,
                        std::vector<EventLogRecordData>& out)
        {
            HANDLE log = OpenEventLogW(nullptr, log_name);
            if (log == nullptr)
                return;

            DWORD oldest = 0, count = 0;
            if (!GetOldestEventLogRecord(log, &oldest) || !GetNumberOfEventLogRecords(log, &count))
            {
                CloseEventLog(log);
                return;
            }
            const std::uint32_t newest_next = (count == 0) ? oldest : (oldest + count); // one past newest

            if (!seeded)
            {
                cursor = newest_next;
                CloseEventLog(log);
                return;
            }
            // Resync when the cursor falls outside the current record range: below oldest = records
            // aged out; above newest = the log was cleared (RecordNumbers restart from 1), which would
            // otherwise stall the cursor forever and silently drop every new event.
            if (cursor < oldest || cursor > newest_next)
                cursor = oldest;

            std::vector<BYTE> buffer(64 * 1024);
            DWORD flags = EVENTLOG_SEEK_READ | EVENTLOG_FORWARDS_READ;
            while (cursor < newest_next)
            {
                DWORD bytes_read = 0, bytes_needed = 0;
                if (!ReadEventLogW(log, flags, cursor, buffer.data(),
                                   static_cast<DWORD>(buffer.size()), &bytes_read, &bytes_needed))
                {
                    if (GetLastError() == ERROR_INSUFFICIENT_BUFFER && bytes_needed > buffer.size())
                    {
                        buffer.resize(bytes_needed);
                        continue;
                    }
                    break; // EOF or error
                }
                flags = EVENTLOG_SEQUENTIAL_READ | EVENTLOG_FORWARDS_READ; // after the initial seek

                BYTE* p = buffer.data();
                BYTE* const stop = buffer.data() + bytes_read;
                while (p + sizeof(EVENTLOGRECORD) <= stop)
                {
                    auto* rec = reinterpret_cast<EVENTLOGRECORD*>(p);
                    // Trust rec->Length only if it is sane and the whole record fits in the bytes we
                    // actually read; a truncated/garbage tail could otherwise re-parse overlapping
                    // memory or walk strings past the record.
                    if (rec->Length < sizeof(EVENTLOGRECORD) || p + rec->Length > stop)
                        break;

                    const BYTE* const rec_end = p + rec->Length;

                    const bool is_error = rec->EventType == EVENTLOG_ERROR_TYPE;
                    const bool is_warning = rec->EventType == EVENTLOG_WARNING_TYPE;
                    if (is_error || is_warning)
                    {
                        EventLogRecordData data;
                        data.kind = is_error ? error_kind : warning_kind;
                        // Full 32-bit event identifier — matches managed record.InstanceId.ToString().
                        // (The low 16 bits alone would be the legacy EventLogEntry.EventID.)
                        data.event_id = std::to_string(rec->EventID);

                        // Every wide-string scan below is length-bounded to rec_end (wcsnlen) and
                        // converted with an explicit length (not -1), so a record whose strings aren't
                        // NUL-terminated within their length can't make the scan read past the record.
                        const auto* const rec_wend = reinterpret_cast<const wchar_t*>(rec_end);

                        // SourceName is a wide string immediately after the fixed struct.
                        const auto* src = reinterpret_cast<const wchar_t*>(p + sizeof(EVENTLOGRECORD));
                        const int src_len = static_cast<int>(wcsnlen(src, static_cast<std::size_t>(rec_wend - src)));
                        const int src_need = src_len > 0 ? WideCharToMultiByte(CP_UTF8, 0, src, src_len, nullptr, 0, nullptr, nullptr) : 0;
                        if (src_need > 0)
                        {
                            data.source.resize(static_cast<std::size_t>(src_need));
                            WideCharToMultiByte(CP_UTF8, 0, src, src_len, data.source.data(), src_need, nullptr, nullptr);
                        }

                        // Insertion strings: NumStrings null-separated wide strings at StringOffset.
                        const wchar_t* str = reinterpret_cast<const wchar_t*>(p + rec->StringOffset);
                        if (rec->StringOffset >= sizeof(EVENTLOGRECORD) && rec->StringOffset < rec->Length)
                        {
                            for (WORD i = 0; i < rec->NumStrings && str < rec_wend; ++i)
                            {
                                const int slen = static_cast<int>(wcsnlen(str, static_cast<std::size_t>(rec_wend - str)));
                                if (slen > 0)
                                {
                                    const int need = WideCharToMultiByte(CP_UTF8, 0, str, slen, nullptr, 0, nullptr, nullptr);
                                    if (need > 0)
                                    {
                                        std::string piece(static_cast<std::size_t>(need), '\0');
                                        WideCharToMultiByte(CP_UTF8, 0, str, slen, piece.data(), need, nullptr, nullptr);
                                        if (!data.message.empty())
                                            data.message += " ";
                                        data.message += piece;
                                    }
                                }
                                str += slen + 1; // past this string + its NUL (bounded)
                            }
                        }

                        out.push_back(std::move(data));
                    }

                    cursor = rec->RecordNumber + 1;
                    p += rec->Length;
                }
            }

            CloseEventLog(log);
        }
    } // namespace

    std::vector<EventLogRecordData> WindowsEventLogReader::PollNew()
    {
        std::vector<EventLogRecordData> out;
        PollOneLog(L"Application", app_cursor_, seeded_, EventLogKind::ApplicationError, EventLogKind::ApplicationWarning, out);
        PollOneLog(L"System", sys_cursor_, seeded_, EventLogKind::SystemError, EventLogKind::SystemWarning, out);
        seeded_ = true;
        return out;
    }

    int ReadWindowsServiceStatus(const std::string& service_name)
    {
        // UTF-8 service name -> wide (Win32 service-name lookups are case-insensitive).
        const int wlen = MultiByteToWideChar(CP_UTF8, 0, service_name.c_str(), -1, nullptr, 0);
        if (wlen <= 1)
            return -1;
        std::wstring wname(static_cast<std::size_t>(wlen - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, service_name.c_str(), -1, wname.data(), wlen);

        SC_HANDLE scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (scm == nullptr)
            return -1;

        SC_HANDLE service = OpenServiceW(scm, wname.c_str(), SERVICE_QUERY_STATUS);
        int result = -1;
        if (service != nullptr)
        {
            SERVICE_STATUS status{};
            if (QueryServiceStatus(service, &status))
                result = static_cast<int>(status.dwCurrentState); // SERVICE_* values == ServiceControllerStatus 1..7
            CloseServiceHandle(service);
        }
        CloseServiceHandle(scm);
        return result;
    }
} // namespace hsm::collector

#else // !_WIN32

namespace hsm::collector
{
    WindowsInfoSample ReadWindowsInfo()
    {
        return WindowsInfoSample{};
    }
    std::vector<EventLogRecordData> WindowsEventLogReader::PollNew()
    {
        return {};
    }
    int ReadWindowsServiceStatus(const std::string&)
    {
        return -1;
    }
} // namespace hsm::collector

#endif // _WIN32
