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

        // Harden on every start, not only when creating: agents installed before #1279 already have a
        // data dir that inherits BUILTIN\Users read from %ProgramData%, and this is what repairs them —
        // the re-propagation tightens the existing config.json and log files in place.
        const auto harden = [acl_errors](const std::wstring& path) {
            std::string error;
            if (!ApplyAgentDirectoryDacl(path, error) && acl_errors != nullptr)
                acl_errors->push_back(error);
        };

        CreateDirectoryW(dir.c_str(), nullptr); // ignore "already exists"

        // A data dir that is a junction / symlink (a non-admin may have pre-created one) is not ours:
        // do not create logs\ through it and do not harden anything behind it. ApplyAgentDirectoryDacl
        // refuses the reparse point and its error is what gets reported.
        const DWORD dir_attributes = GetFileAttributesW(dir.c_str());
        if (dir_attributes != INVALID_FILE_ATTRIBUTES && (dir_attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            harden(dir);
            return true;
        }

        // Create logs\ before hardening: once the data dir is hardened, a non-elevated console run could
        // no longer create it.
        CreateDirectoryW(logs.c_str(), nullptr);
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

    std::wstring UpdateStatePath()
    {
        return ProgramDataAgentDir() + L"\\update-state.txt";
    }

    std::wstring CurrentExePath()
    {
        std::wstring path(MAX_PATH, L'\0');
        for (;;)
        {
            const DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
            if (length == 0)
                return std::wstring{};
            if (length < path.size())
            {
                path.resize(length);
                return path;
            }
            path.resize(path.size() * 2); // truncated -> grow and retry
        }
    }

    UpdateFiles UpdateFilesFor(const std::wstring& running_exe)
    {
        UpdateFiles files;
        const std::size_t slash = running_exe.find_last_of(L"\\/");
        if (slash == std::wstring::npos || slash == 0)
            return files; // not a full path: no install directory to act on

        files.dir = running_exe.substr(0, slash);
        files.exe = running_exe;
        files.new_exe = files.dir + L"\\hsm-agent.new.exe";
        files.old_exe = files.dir + L"\\hsm-agent.old.exe";
        return files;
    }

    UpdateFiles CurrentUpdateFiles()
    {
        return UpdateFilesFor(CurrentExePath());
    }

    std::wstring InstallExePath()
    {
        return CurrentUpdateFiles().exe;
    }

    std::wstring NewExePath()
    {
        return CurrentUpdateFiles().new_exe;
    }

    std::wstring OldExePath()
    {
        return CurrentUpdateFiles().old_exe;
    }
} // namespace hsm::agent
