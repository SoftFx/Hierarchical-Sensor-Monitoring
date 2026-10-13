#pragma once

/// @file
/// @brief Protected DACL for the agent data directory (#1279): only NT AUTHORITY\SYSTEM and
/// BUILTIN\Administrators may access `%ProgramData%\HSM Agent`, because `config.json` there holds the
/// product access key in cleartext. Windows-only.

#include <string>

namespace hsm::agent
{
    /// Replace the DACL of directory `path` with a **protected** one (inheritance from the parent is
    /// blocked) that grants full control to `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators` only,
    /// both inheritable to sub-folders and files (OI)(CI). Every other ACE — `Users`,
    /// `Authenticated Users`, `Everyone`, `CREATOR OWNER`, any operator-added ACE — is dropped.
    ///
    /// Trustees are built from well-known SIDs (`CreateWellKnownSid`), never account names, so the
    /// helper works on localized Windows. `SetNamedSecurityInfoW` propagates the new inheritable ACEs
    /// to existing children, so files that inherited a wide ACL (agents installed before #1279) are
    /// tightened by the same call; a child's own explicit ACEs are left alone.
    ///
    /// The directory's owner is set to `BUILTIN\Administrators` in the same call (an owner keeps an
    /// implicit WRITE_DAC, so a non-admin owner could re-open the DACL). If the owner change is refused
    /// the DACL is still applied, and the call returns false with `error` naming the owner failure.
    ///
    /// A `path` that is a reparse point (junction / symlink — possibly planted by a non-admin) is left
    /// untouched: the call returns false with an error saying so, rather than change the security of
    /// whatever the link points at.
    ///
    /// Idempotent: re-applying to an already-hardened directory changes nothing. Returns false and
    /// fills `error` (UTF-8, includes the Win32 error code) on any failure; never throws.
    bool ApplyAgentDirectoryDacl(const std::wstring& path, std::string& error);
} // namespace hsm::agent
