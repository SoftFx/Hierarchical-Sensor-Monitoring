#include "windows_info.hpp"

#ifdef _WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

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

        return s;
    }
}

#else // !_WIN32

namespace hsm::collector
{
    WindowsInfoSample ReadWindowsInfo() { return WindowsInfoSample{}; }
}

#endif // _WIN32
