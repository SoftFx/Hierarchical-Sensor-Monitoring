#include "agent/dir_acl.hpp"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <aclapi.h>

namespace hsm::agent
{
    namespace
    {
        std::string Narrow(const std::wstring& wide)
        {
            if (wide.empty())
                return {};
            const int needed = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), nullptr, 0, nullptr, nullptr);
            if (needed <= 0)
                return {};
            std::string utf8(static_cast<std::size_t>(needed), '\0');
            WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), static_cast<int>(wide.size()), utf8.data(), needed, nullptr, nullptr);
            return utf8;
        }

        std::string Failure(const std::wstring& path, const char* step, DWORD code)
        {
            return "cannot restrict access to '" + Narrow(path) + "': " + step + " failed (Win32 error " + std::to_string(code) + ")";
        }

        // Full control for `sid`, inherited by sub-folders and files — icacls shows it as (OI)(CI)(F).
        void FullControl(EXPLICIT_ACCESS_W& entry, PSID sid)
        {
            entry.grfAccessPermissions = FILE_ALL_ACCESS;
            entry.grfAccessMode = SET_ACCESS;
            entry.grfInheritance = SUB_CONTAINERS_AND_OBJECTS_INHERIT;
            entry.Trustee.TrusteeForm = TRUSTEE_IS_SID;
            entry.Trustee.TrusteeType = TRUSTEE_IS_WELL_KNOWN_GROUP;
            entry.Trustee.ptstrName = static_cast<LPWSTR>(sid);
        }
    } // namespace

    bool ApplyAgentDirectoryDacl(const std::wstring& path, std::string& error)
    {
        // Well-known SIDs, never names: "BUILTIN\Administrators" is localized on non-English Windows.
        BYTE system_sid[SECURITY_MAX_SID_SIZE];
        BYTE admins_sid[SECURITY_MAX_SID_SIZE];
        DWORD system_size = sizeof(system_sid);
        DWORD admins_size = sizeof(admins_sid);
        if (!CreateWellKnownSid(WinLocalSystemSid, nullptr, system_sid, &system_size))
        {
            error = Failure(path, "CreateWellKnownSid(SYSTEM)", GetLastError());
            return false;
        }
        if (!CreateWellKnownSid(WinBuiltinAdministratorsSid, nullptr, admins_sid, &admins_size))
        {
            error = Failure(path, "CreateWellKnownSid(Administrators)", GetLastError());
            return false;
        }

        EXPLICIT_ACCESS_W entries[2] = {};
        FullControl(entries[0], system_sid);
        FullControl(entries[1], admins_sid);

        // No old ACL to merge: the new DACL holds exactly these two ACEs.
        PACL dacl = nullptr;
        const DWORD built = SetEntriesInAclW(2, entries, nullptr, &dacl);
        if (built != ERROR_SUCCESS)
        {
            error = Failure(path, "SetEntriesInAclW", built);
            return false;
        }

        // PROTECTED_DACL_SECURITY_INFORMATION sets SE_DACL_PROTECTED, so the ACEs %ProgramData% hands
        // down (BUILTIN\Users read, CREATOR OWNER, ...) are no longer inherited. The call also
        // re-propagates the new inheritable ACEs to existing children (config.json, logs\), which is
        // what repairs an install made before #1279.
        const DWORD set = SetNamedSecurityInfoW(const_cast<LPWSTR>(path.c_str()), SE_FILE_OBJECT,
                                                DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION,
                                                nullptr, nullptr, dacl, nullptr);
        LocalFree(dacl);
        if (set != ERROR_SUCCESS)
        {
            error = Failure(path, "SetNamedSecurityInfoW", set);
            return false;
        }
        return true;
    }
} // namespace hsm::agent
