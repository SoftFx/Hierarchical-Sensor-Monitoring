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
        //
        // The owner is reset to BUILTIN\Administrators in the same call: an owner keeps an implicit
        // WRITE_DAC whatever the DACL says, so a directory pre-created by a non-admin (Users may create
        // folders in %ProgramData%) could otherwise be re-opened by that user. LocalSystem and an
        // elevated admin both hold Administrators as an owner-eligible group.
        constexpr SECURITY_INFORMATION kDacl = DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION;
        LPWSTR native_path = const_cast<LPWSTR>(path.c_str());
        const DWORD with_owner = SetNamedSecurityInfoW(native_path, SE_FILE_OBJECT, kDacl | OWNER_SECURITY_INFORMATION,
                                                       admins_sid, nullptr, dacl, nullptr);
        if (with_owner == ERROR_SUCCESS)
        {
            LocalFree(dacl);
            return true;
        }

        // Owner change refused (e.g. an unelevated console run: Administrators is deny-only in its
        // token, ERROR_INVALID_OWNER). Still restrict the DACL — that is what keeps the key from local
        // users — and report the owner failure.
        const DWORD dacl_only = SetNamedSecurityInfoW(native_path, SE_FILE_OBJECT, kDacl, nullptr, nullptr, dacl, nullptr);
        LocalFree(dacl);
        if (dacl_only != ERROR_SUCCESS)
        {
            error = Failure(path, "SetNamedSecurityInfoW", dacl_only);
            return false;
        }
        error = "restricted access to '" + Narrow(path) + "' to SYSTEM + Administrators, but could not make BUILTIN\\Administrators its owner: SetNamedSecurityInfoW failed (Win32 error " + std::to_string(with_owner) + ")";
        return false;
    }
} // namespace hsm::agent
