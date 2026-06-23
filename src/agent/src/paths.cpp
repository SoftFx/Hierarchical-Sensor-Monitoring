// _wgetenv is the simplest portable way to read %ProgramData%; the _s variant adds nothing here.
#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#include "agent/paths.hpp"

#include <cstdlib>
#include <fstream>
#include <sstream>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace hsm::agent
{
    std::wstring ProgramDataAgentDir()
    {
        const wchar_t* program_data = _wgetenv(L"ProgramData");
        std::wstring base = (program_data != nullptr && program_data[0] != L'\0') ? program_data : L"C:\\ProgramData";
        return base + L"\\HSM Agent";
    }

    std::wstring DefaultConfigPath()
    {
        return ProgramDataAgentDir() + L"\\config.json";
    }

    std::wstring LogFilePath()
    {
        return ProgramDataAgentDir() + L"\\logs\\hsm-agent.log";
    }

    bool EnsureDirectories()
    {
        const std::wstring dir = ProgramDataAgentDir();
        CreateDirectoryW(dir.c_str(), nullptr); // ignore "already exists"
        CreateDirectoryW((dir + L"\\logs").c_str(), nullptr);
        return GetFileAttributesW(dir.c_str()) != INVALID_FILE_ATTRIBUTES;
    }

    bool ReadTextFileWide(const std::wstring& path, std::string& out)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream)
            return false;
        std::ostringstream buffer;
        buffer << stream.rdbuf();
        out = buffer.str();
        return true;
    }
} // namespace hsm::agent
