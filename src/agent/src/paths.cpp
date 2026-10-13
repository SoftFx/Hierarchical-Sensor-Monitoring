// _wgetenv is the simplest portable way to read %ProgramData%; the _s variant adds nothing here.
#ifndef _CRT_SECURE_NO_WARNINGS
#define _CRT_SECURE_NO_WARNINGS
#endif

#include "agent/paths.hpp"

#include "agent/dir_acl.hpp"

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

    bool EnsureDirectories(std::vector<std::string>* acl_errors)
    {
        const std::wstring dir = ProgramDataAgentDir();
        const std::wstring logs = dir + L"\\logs";

        // Create both first: once the data dir is hardened, a non-elevated console run could no longer
        // create logs\ inside it.
        CreateDirectoryW(dir.c_str(), nullptr); // ignore "already exists"
        CreateDirectoryW(logs.c_str(), nullptr);

        // Harden on every start, not only when creating: agents installed before #1279 already have a
        // data dir that inherits BUILTIN\Users read from %ProgramData%, and this is what repairs them —
        // the re-propagation tightens the existing config.json and log files in place.
        const auto harden = [acl_errors](const std::wstring& path) {
            std::string error;
            if (!ApplyAgentDirectoryDacl(path, error) && acl_errors != nullptr)
                acl_errors->push_back(error);
        };
        harden(dir);
        // logs\ gets the same protected DACL (decision documented in aicontext/features/agent): the
        // log never holds the access key but does carry host / process / service names, and a
        // protected DACL of its own keeps it closed even if the parent's ACL is later loosened by hand.
        harden(logs);

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

    std::wstring ProgramFilesAgentDir()
    {
        const wchar_t* pf = _wgetenv(L"ProgramFiles");
        std::wstring base = (pf != nullptr && pf[0] != L'\0') ? pf : L"C:\\Program Files";
        return base + L"\\HSM Agent";
    }

    std::wstring InstallExePath()
    {
        return ProgramFilesAgentDir() + L"\\hsm-agent.exe";
    }

    std::wstring NewExePath()
    {
        return ProgramFilesAgentDir() + L"\\hsm-agent.new.exe";
    }

    std::wstring OldExePath()
    {
        return ProgramFilesAgentDir() + L"\\hsm-agent.old.exe";
    }
} // namespace hsm::agent
