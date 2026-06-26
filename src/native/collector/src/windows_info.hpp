#pragma once

// Windows OS-info readers (#1189 follow-up): the native collector registered the managed
// "Windows OS info" sensors but never populated them (the periodic metric path is double-only,
// while these are TimeSpan/Version/String). This provides the Win32 reads behind a plain data
// struct so hsm_collector.cpp can post typed values without pulling <windows.h> in.

#include <cstdint>
#include <string>
#include <vector>

namespace hsm::collector
{
    struct WindowsInfoSample
    {
        // Last restart: uptime since boot (TimeSpan), .NET 100-ns ticks.
        bool has_uptime = false;
        std::int64_t uptime_ticks = 0;

        // Install date: age since OS install (TimeSpan), .NET 100-ns ticks.
        bool has_install_age = false;
        std::int64_t install_age_ticks = 0;

        // Last update: age since the last successful Windows Update (TimeSpan), .NET 100-ns ticks.
        bool has_last_update_age = false;
        std::int64_t last_update_age_ticks = 0;

        // Version & patch: Windows full build version (Major.Minor.Build.UBR) + product comment.
        bool has_version = false;
        std::int32_t ver_major = 0;
        std::int32_t ver_minor = 0;
        std::int32_t ver_build = 0;
        std::int32_t ver_ubr = 0;
        std::string version_comment; // "ProductName DisplayVersion (Major.Minor.Build)"
    };

    // Reads the current Windows OS info. Windows-only; returns an all-empty sample on other
    // platforms (each field gated by its has_* flag), so callers stay cross-platform.
    WindowsInfoSample ReadWindowsInfo();

    // Which event-log sensor a record belongs to.
    enum class EventLogKind
    {
        ApplicationError = 0,
        SystemError = 1,
        ApplicationWarning = 2,
        SystemWarning = 3,
    };

    struct EventLogRecordData
    {
        EventLogKind kind = EventLogKind::ApplicationError;
        std::string event_id; // InstanceId (matches managed record.InstanceId.ToString())
        std::string source;   // event source name
        std::string message;  // best-effort: joined insertion strings
    };

    // Stateful poller for the Application/System event logs (mirrors the managed event-driven
    // WindowsLogsSensorBase). The first poll only seeds cursors at the newest record (no backfill);
    // each later poll returns the Error/Warning records written since. Windows-only; a no-op
    // returning {} elsewhere.
    class WindowsEventLogReader
    {
    public:
        std::vector<EventLogRecordData> PollNew();

    private:
        std::uint32_t app_cursor_ = 0; // next unread RecordNumber for "Application"
        std::uint32_t sys_cursor_ = 0; // next unread RecordNumber for "System"
        bool seeded_ = false;
    };
}
