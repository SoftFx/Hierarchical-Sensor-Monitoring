#include "agent/event_log.hpp"

#include <utility>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace hsm::agent
{
    namespace
    {
        constexpr const wchar_t* kSourceName = L"HSMAgent";
        constexpr const wchar_t* kRegKey = L"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Application\\HSMAgent";

        std::wstring Widen(const std::string& utf8)
        {
            if (utf8.empty())
                return std::wstring{};
            const int needed = MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
            if (needed <= 0)
                return std::wstring{};
            std::wstring wide(static_cast<std::size_t>(needed), L'\0');
            MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), wide.data(), needed);
            return wide;
        }
    } // namespace

    EventLogSink::EventLogSink(std::wstring source_name)
    {
        handle_ = RegisterEventSourceW(nullptr, source_name.empty() ? kSourceName : source_name.c_str());
    }

    EventLogSink::~EventLogSink()
    {
        if (handle_ != nullptr)
            ::DeregisterEventSource(static_cast<HANDLE>(handle_)); // Win32 close, not our registry helper
    }

    void EventLogSink::Report(unsigned short event_type, const std::string& message)
    {
        if (handle_ == nullptr)
            return;

        const std::wstring wide = Widen(message);
        const wchar_t* strings[1] = { wide.c_str() };
        ReportEventW(static_cast<HANDLE>(handle_), event_type, 0, 0, nullptr, 1, 0, strings, nullptr);
    }

    void EventLogSink::Write(hsm::collector::LogLevel level, const std::string& message)
    {
        if (level == hsm::collector::LogLevel::Error)
            Report(EVENTLOG_ERROR_TYPE, message);
    }

    void EventLogSink::ReportInformation(const std::string& message)
    {
        Report(EVENTLOG_INFORMATION_TYPE, message);
    }

    void EventLogSink::ReportError(const std::string& message)
    {
        Report(EVENTLOG_ERROR_TYPE, message);
    }

    bool RegisterEventSourceKey(const std::wstring& exe_path)
    {
        HKEY key = nullptr;
        LONG status = RegCreateKeyExW(
            HKEY_LOCAL_MACHINE, kRegKey, 0, nullptr, REG_OPTION_NON_VOLATILE, KEY_SET_VALUE, nullptr, &key, nullptr);
        if (status != ERROR_SUCCESS)
            return false;

        // Point the message file at the exe itself. The exe has no embedded message table, so the
        // Event Viewer shows the inserted text with a generic wrapper note — acceptable for the
        // foundation; a dedicated message resource is a follow-up.
        RegSetValueExW(
            key,
            L"EventMessageFile",
            0,
            REG_EXPAND_SZ,
            reinterpret_cast<const BYTE*>(exe_path.c_str()),
            static_cast<DWORD>((exe_path.size() + 1) * sizeof(wchar_t)));

        DWORD types_supported = EVENTLOG_ERROR_TYPE | EVENTLOG_WARNING_TYPE | EVENTLOG_INFORMATION_TYPE;
        RegSetValueExW(
            key, L"TypesSupported", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&types_supported), sizeof(types_supported));

        RegCloseKey(key);
        return true;
    }

    bool UnregisterEventSourceKey()
    {
        const LONG status = RegDeleteKeyW(HKEY_LOCAL_MACHINE, kRegKey);
        return status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND;
    }
} // namespace hsm::agent
