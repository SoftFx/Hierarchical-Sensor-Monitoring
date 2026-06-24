// HSM Agent — --apply-update detached helper (epic #1174).
//
// This process is spawned by the running service after it downloads and verifies a new exe.
// It must be detached (DETACHED_PROCESS) so it survives when the service process exits.
//
// Binary swap dance:
//   1. Wait up to 60 s for service HSMAgent to reach STOPPED.
//   2. MoveFileEx: hsm-agent.exe → hsm-agent.old.exe   (rename, not delete; rollback target)
//   3. MoveFileEx: hsm-agent.new.exe → hsm-agent.exe    (atomic rename)
//   4. sc start HSMAgent (or StartService).
//   5. Health gate: poll status for up to 30 s; if STOPPED/FAILED → rollback.
//
// Rollback:
//   MoveFileEx: hsm-agent.exe → (delete or temp), hsm-agent.old.exe → hsm-agent.exe
//   Attempt to restart the old version.
//
// On success the service cleans up hsm-agent.old.exe at next startup.

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winsvc.h>

#include "agent/apply_update.hpp"
#include "agent/paths.hpp"

#include <cstdio>
#include <iostream>
#include <string>

static const wchar_t* kServiceName = L"HSMAgent";

// ---- helpers ---------------------------------------------------------------------------------

static void Log(const std::string& msg)
{
    // This runs as a detached console-less process. Write to stderr (may be lost) + Event Log.
    std::cerr << "[apply-update] " << msg << '\n';

    HANDLE hev = RegisterEventSourceW(nullptr, L"HSMAgent");
    if (hev)
    {
        const std::wstring wmsg(msg.begin(), msg.end());
        const wchar_t* strs[] = { wmsg.c_str() };
        ReportEventW(hev, EVENTLOG_INFORMATION_TYPE, 0, 0, nullptr, 1, 0, strs, nullptr);
        DeregisterEventSource(hev);
    }
}

static void LogErr(const std::string& msg)
{
    std::cerr << "[apply-update] ERROR: " << msg << '\n';

    HANDLE hev = RegisterEventSourceW(nullptr, L"HSMAgent");
    if (hev)
    {
        const std::wstring wmsg(msg.begin(), msg.end());
        const wchar_t* strs[] = { wmsg.c_str() };
        ReportEventW(hev, EVENTLOG_ERROR_TYPE, 0, 0, nullptr, 1, 0, strs, nullptr);
        DeregisterEventSource(hev);
    }
}

// Wait until the service status matches `target_state` or timeout_ms elapses.
static bool WaitForServiceState(SC_HANDLE svc, DWORD target_state, DWORD timeout_ms)
{
    const DWORD deadline = GetTickCount() + timeout_ms;
    while (true)
    {
        SERVICE_STATUS ss = {};
        if (!QueryServiceStatus(svc, &ss))
            return false;
        if (ss.dwCurrentState == target_state)
            return true;
        if (GetTickCount() >= deadline)
            return false;
        Sleep(500);
    }
}

namespace hsm::agent
{
    int RunApplyUpdate()
    {
        const std::wstring install_exe = InstallExePath();
        const std::wstring new_exe = NewExePath();
        const std::wstring old_exe = OldExePath();

        // Verify that the downloaded binary exists.
        if (GetFileAttributesW(new_exe.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            LogErr("hsm-agent.new.exe not found — nothing to apply");
            return 1;
        }

        // --- Step 1: wait for the service to stop -------------------------------------------
        SC_HANDLE scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (!scm)
        {
            LogErr("cannot open SCM");
            return 1;
        }

        SC_HANDLE svc = OpenServiceW(scm, kServiceName, SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP);
        if (!svc)
        {
            CloseServiceHandle(scm);
            LogErr("cannot open HSMAgent service");
            return 1;
        }

        Log("waiting for HSMAgent to stop (up to 60 s)...");
        if (!WaitForServiceState(svc, SERVICE_STOPPED, 60000))
        {
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            LogErr("HSMAgent did not stop within 60 s — aborting update");
            DeleteFileW(new_exe.c_str());
            return 1;
        }
        Log("HSMAgent stopped");

        // --- Step 2 + 3: rename dance -------------------------------------------------------
        // Remove any stale .old.exe first.
        DeleteFileW(old_exe.c_str());

        if (!MoveFileExW(install_exe.c_str(), old_exe.c_str(), MOVEFILE_REPLACE_EXISTING))
        {
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            LogErr("cannot rename hsm-agent.exe → hsm-agent.old.exe");
            DeleteFileW(new_exe.c_str());
            return 1;
        }

        if (!MoveFileExW(new_exe.c_str(), install_exe.c_str(), MOVEFILE_REPLACE_EXISTING))
        {
            // Try to restore the old binary.
            MoveFileExW(old_exe.c_str(), install_exe.c_str(), MOVEFILE_REPLACE_EXISTING);
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            LogErr("cannot rename hsm-agent.new.exe → hsm-agent.exe; rolled back");
            return 1;
        }

        Log("binary swap complete");

        // --- Step 4: start the service ------------------------------------------------------
        if (!StartServiceW(svc, 0, nullptr))
        {
            const DWORD err = GetLastError();
            if (err != ERROR_SERVICE_ALREADY_RUNNING)
            {
                // Rollback.
                MoveFileExW(install_exe.c_str(), new_exe.c_str(), MOVEFILE_REPLACE_EXISTING);
                MoveFileExW(old_exe.c_str(), install_exe.c_str(), MOVEFILE_REPLACE_EXISTING);
                StartServiceW(svc, 0, nullptr);
                CloseServiceHandle(svc);
                CloseServiceHandle(scm);
                LogErr("StartService failed (" + std::to_string(err) + "); rolled back");
                return 1;
            }
        }

        // --- Step 5: health gate (30 s) -----------------------------------------------------
        Log("health gate: waiting 30 s for service to reach RUNNING...");
        if (!WaitForServiceState(svc, SERVICE_RUNNING, 30000))
        {
            LogErr("new version did not reach RUNNING within 30 s — rolling back");

            // Stop the new (presumably crashed) service.
            SERVICE_STATUS ss = {};
            ControlService(svc, SERVICE_CONTROL_STOP, &ss);
            WaitForServiceState(svc, SERVICE_STOPPED, 15000);

            // Restore the old binary.
            MoveFileExW(install_exe.c_str(), new_exe.c_str(), MOVEFILE_REPLACE_EXISTING);
            MoveFileExW(old_exe.c_str(), install_exe.c_str(), MOVEFILE_REPLACE_EXISTING);
            StartServiceW(svc, 0, nullptr);

            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            LogErr("rolled back to previous version");
            return 1;
        }

        Log("HSMAgent running with new version — update successful");
        CloseServiceHandle(svc);
        CloseServiceHandle(scm);
        return 0;
    }

} // namespace hsm::agent
