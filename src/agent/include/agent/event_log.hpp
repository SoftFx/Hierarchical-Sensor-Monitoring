#pragma once

/// @file
/// @brief Windows Event Log sink (source "HSMAgent"). WARN/ERROR-class messages land in the
/// Application log so an operator sees agent failures without the log file.

#include <hsm_collector/enums.hpp>

#include <string>

namespace hsm::agent
{
    /// RAII wrapper around a registered event source handle. Constructing registers the source for
    /// reporting; the destructor deregisters it. `Write` forwards only Error-level messages (the
    /// noisy stream goes to the file log); `Report*` emit explicit lifecycle entries.
    class EventLogSink
    {
    public:
        explicit EventLogSink(std::wstring source_name = L"HSMAgent");
        ~EventLogSink();

        EventLogSink(const EventLogSink&) = delete;
        EventLogSink& operator=(const EventLogSink&) = delete;

        /// Forward a collector log line: Error → event-log error entry; Info/Debug ignored here.
        void Write(hsm::collector::LogLevel level, const std::string& message);

        /// Explicit informational lifecycle entry (service started / stopped).
        void ReportInformation(const std::string& message);

        /// Explicit error entry (e.g. fatal startup failure before the collector logs).
        void ReportError(const std::string& message);

    private:
        void Report(unsigned short event_type, const std::string& message);

        void* handle_ = nullptr; // HANDLE from RegisterEventSourceW
    };

    /// Create/refresh the registry key that lets the Event Viewer resolve the "HSMAgent" source to
    /// `exe_path`. Called from --install (needs elevation). Returns false on failure.
    /// (Named with a `Key` suffix to avoid the Win32 `RegisterEventSource` macro.)
    bool RegisterEventSourceKey(const std::wstring& exe_path);

    /// Remove the "HSMAgent" event-source registry key. Called from --uninstall.
    bool UnregisterEventSourceKey();
} // namespace hsm::agent
