#pragma once

// Windows OS-info readers (#1189 follow-up): the native collector registered the managed
// "Windows OS info" sensors but never populated them (the periodic metric path is double-only,
// while these are TimeSpan/Version/String). This provides the Win32 reads behind a plain data
// struct so hsm_collector.cpp can post typed values without pulling <windows.h> in.

#include <cstdint>
#include <string>

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
}
