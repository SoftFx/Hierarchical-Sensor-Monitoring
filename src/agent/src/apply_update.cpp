// HSM Agent — --apply-update detached helper (epic #1174, #1812).
//
// This process is spawned by the running service after it downloads and verifies a new exe.
// It must be detached (DETACHED_PROCESS) so it survives when the service process exits. It is
// spawned from the installed exe, so the folder it works in (the directory of its own exe) is the
// folder the update checker staged the download in.
//
// Binary swap dance (ApplyStagedUpdate):
//   1. Wait up to 60 s for service HSMAgent to reach STOPPED (and its process to exit).
//   2. MoveFileEx: hsm-agent.exe → hsm-agent.old.exe   (rename, not delete; rollback target)
//   3. MoveFileEx: hsm-agent.new.exe → hsm-agent.exe
//   4. StartService.
//   5. Health gate: wait up to 30 s for RUNNING; otherwise stop it and roll back.
//
// Every failure after the service stopped (nothing staged, a rename, the start, the health gate)
// puts the old binary back and starts the service again: a failed update never leaves it stopped.
// The update checker recorded this attempt before it stopped the service; a successful swap clears
// that record, a failed one leaves it so the next attempt of the same version backs off
// (update_state.hpp).
//
// On success the service cleans up hsm-agent.old.exe at next startup.

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winsvc.h>

#include "agent/apply_update.hpp"
#include "agent/paths.hpp"
#include "agent/update_state.hpp"

#include <cstdio>
#include <iostream>
#include <string>

static const wchar_t* kServiceName = L"HSMAgent";

// ---- helpers ---------------------------------------------------------------------------------

static std::wstring Widen(const std::string& s)
{
    if (s.empty())
        return {};
    const int needed = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    if (needed <= 0)
        return {};
    std::wstring w(static_cast<std::size_t>(needed - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, w.data(), needed);
    return w;
}

static std::string Narrow(const std::wstring& w)
{
    if (w.empty())
        return {};
    const int needed = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (needed <= 0)
        return {};
    std::string s(static_cast<std::size_t>(needed - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, s.data(), needed, nullptr, nullptr);
    return s;
}

static void Report(WORD type, const std::string& msg)
{
    HANDLE hev = RegisterEventSourceW(nullptr, L"HSMAgent");
    if (hev)
    {
        const std::wstring wmsg = Widen(msg);
        const wchar_t* strs[] = { wmsg.c_str() };
        ReportEventW(hev, type, 0, 0, nullptr, 1, 0, strs, nullptr);
        DeregisterEventSource(hev);
    }
}

static void Log(const std::string& msg)
{
    // This runs as a detached console-less process. Write to stderr (may be lost) + Event Log.
    std::cerr << "[apply-update] " << msg << '\n';
    Report(EVENTLOG_INFORMATION_TYPE, msg);
}

static void LogErr(const std::string& msg)
{
    std::cerr << "[apply-update] ERROR: " << msg << '\n';
    Report(EVENTLOG_ERROR_TYPE, msg);
}

// Wait until the service status matches `target_state` or timeout_ms elapses.
// Uses GetTickCount64() to avoid the 32-bit wrap every ~49 days.
static bool WaitForServiceState(SC_HANDLE svc, DWORD target_state, DWORD timeout_ms)
{
    const ULONGLONG deadline = GetTickCount64() + timeout_ms;
    while (true)
    {
        SERVICE_STATUS ss = {};
        if (!QueryServiceStatus(svc, &ss))
            return false;
        if (ss.dwCurrentState == target_state)
            return true;
        if (GetTickCount64() >= deadline)
            return false;
        Sleep(500);
    }
}

static bool Exists(const std::wstring& path)
{
    return !path.empty() && GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}

static bool Move(const std::wstring& from, const std::wstring& to)
{
    return MoveFileExW(from.c_str(), to.c_str(), MOVEFILE_REPLACE_EXISTING) != FALSE;
}

static std::string LastError()
{
    return std::to_string(GetLastError());
}

namespace
{
    // The real HSMAgent service behind the ServiceControl seam.
    class ScmServiceControl final : public hsm::agent::ServiceControl
    {
    public:
        ScmServiceControl()
        {
            scm_ = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
            if (scm_ != nullptr)
                svc_ = OpenServiceW(scm_, kServiceName, SERVICE_QUERY_STATUS | SERVICE_START | SERVICE_STOP);
            // Remember the process that runs the service now (the agent that spawned this helper):
            // after STOPPED it still holds the single-instance mutex until it exits, and a start in
            // that window would fail the health gate.
            SERVICE_STATUS_PROCESS status = {};
            QueryStatus(status);
        }

        ~ScmServiceControl() override
        {
            if (process_ != nullptr)
                CloseHandle(process_);
            if (svc_ != nullptr)
                CloseServiceHandle(svc_);
            if (scm_ != nullptr)
                CloseServiceHandle(scm_);
        }

        ScmServiceControl(const ScmServiceControl&) = delete;
        ScmServiceControl& operator=(const ScmServiceControl&) = delete;

        bool Valid() const { return svc_ != nullptr; }

        bool WaitUntilStopped(unsigned timeout_ms) override
        {
            const ULONGLONG deadline = GetTickCount64() + timeout_ms;
            if (!WaitForState(SERVICE_STOPPED, deadline))
                return false;
            WaitForProcessExit(deadline);
            return true;
        }

        bool Start() override
        {
            if (StartServiceW(svc_, 0, nullptr))
                return true;
            const DWORD err = GetLastError();
            if (err == ERROR_SERVICE_ALREADY_RUNNING)
                return true;
            LogErr("StartService failed (" + std::to_string(err) + ")");
            return false;
        }

        bool WaitUntilRunning(unsigned timeout_ms) override
        {
            return WaitForState(SERVICE_RUNNING, GetTickCount64() + timeout_ms);
        }

        void Stop(unsigned timeout_ms) override
        {
            const ULONGLONG deadline = GetTickCount64() + timeout_ms;
            SERVICE_STATUS ss = {};
            ControlService(svc_, SERVICE_CONTROL_STOP, &ss);
            if (WaitForState(SERVICE_STOPPED, deadline))
                WaitForProcessExit(deadline);
        }

    private:
        // Query the status and keep a handle to the process that runs the service.
        bool QueryStatus(SERVICE_STATUS_PROCESS& status)
        {
            if (svc_ == nullptr)
                return false;
            DWORD needed = 0;
            if (!QueryServiceStatusEx(svc_, SC_STATUS_PROCESS_INFO, reinterpret_cast<LPBYTE>(&status), sizeof(status), &needed))
                return false;
            if (status.dwProcessId != 0 && status.dwProcessId != process_id_ && status.dwProcessId != GetCurrentProcessId())
            {
                if (process_ != nullptr)
                    CloseHandle(process_);
                process_ = OpenProcess(SYNCHRONIZE, FALSE, status.dwProcessId);
                process_id_ = status.dwProcessId;
            }
            return true;
        }

        // Polls until `target` or the deadline. Waiting for RUNNING ends early (false) when the
        // service stopped instead.
        bool WaitForState(DWORD target, ULONGLONG deadline)
        {
            while (true)
            {
                SERVICE_STATUS_PROCESS status = {};
                if (!QueryStatus(status))
                    return false;
                if (status.dwCurrentState == target)
                    return true;
                if (target == SERVICE_RUNNING && status.dwCurrentState == SERVICE_STOPPED)
                    return false;
                if (GetTickCount64() >= deadline)
                    return false;
                Sleep(500);
            }
        }

        // Best effort: a process that outlives the deadline is not waited for further; a start that
        // then collides with it fails the health gate and is retried by the rollback.
        void WaitForProcessExit(ULONGLONG deadline)
        {
            if (process_ == nullptr)
                return;
            const ULONGLONG now = GetTickCount64();
            const DWORD left = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
            if (WaitForSingleObject(process_, left) == WAIT_OBJECT_0)
            {
                CloseHandle(process_);
                process_ = nullptr;
            }
        }

        SC_HANDLE scm_ = nullptr;
        SC_HANDLE svc_ = nullptr;
        HANDLE process_ = nullptr;
        DWORD process_id_ = 0;
    };
} // namespace

namespace hsm::agent
{
    namespace
    {
        constexpr unsigned kStopTimeoutMs = 60000;
        constexpr unsigned kHealthGateMs = 30000;
        constexpr unsigned kRollbackStopMs = 15000;
        constexpr int kStartAttempts = 3;
    } // namespace

    int ApplyStagedUpdate(const UpdateFiles& files, ServiceControl& service, const std::filesystem::path& state_path,
                          const ApplyLogFn& log)
    {
        const auto info = [&log](const std::string& m) { if (log) log(false, m); };
        const auto error = [&log](const std::string& m) { if (log) log(true, m); };

        // The service must end up running, whatever failed. Start() is a no-op success when it still
        // runs; each attempt waits for RUNNING, so a start that collides with an exiting process
        // gets another chance.
        const auto start_again = [&]() {
            for (int attempt = 1; attempt <= kStartAttempts; ++attempt)
            {
                if (service.Start() && service.WaitUntilRunning(kHealthGateMs))
                {
                    info("HSMAgent is running on the previous version");
                    return;
                }
            }
            error("HSMAgent could not be started again after a failed update; start it by hand (Start-Service HSMAgent)");
        };

        if (files.exe.empty())
        {
            error("cannot resolve the install directory of hsm-agent.exe; no update applied");
            service.WaitUntilStopped(kStopTimeoutMs);
            start_again();
            return 1;
        }

        info("waiting for HSMAgent to stop (up to 60 s)...");
        if (!service.WaitUntilStopped(kStopTimeoutMs))
        {
            error("HSMAgent did not stop within 60 s; update abandoned");
            DeleteFileW(files.new_exe.c_str());
            start_again();
            return 1;
        }
        info("HSMAgent stopped");

        if (!Exists(files.new_exe))
        {
            error("nothing staged to apply (" + Narrow(files.new_exe) + " not found); starting HSMAgent again");
            start_again();
            return 1;
        }

        // Remove any stale .old.exe first.
        DeleteFileW(files.old_exe.c_str());

        if (!Move(files.exe, files.old_exe))
        {
            error("cannot rename " + Narrow(files.exe) + " to hsm-agent.old.exe (error " + LastError() + "); update abandoned");
            DeleteFileW(files.new_exe.c_str());
            start_again();
            return 1;
        }

        if (!Move(files.new_exe, files.exe))
        {
            error("cannot rename hsm-agent.new.exe to " + Narrow(files.exe) + " (error " + LastError() + "); rolling back");
            if (!Move(files.old_exe, files.exe))
                error("rollback: cannot restore " + Narrow(files.exe) + " (error " + LastError() + ")");
            DeleteFileW(files.new_exe.c_str());
            start_again();
            return 1;
        }

        info("binary swap complete in " + Narrow(files.dir));

        info("health gate: waiting 30 s for HSMAgent to reach RUNNING...");
        if (!service.Start() || !service.WaitUntilRunning(kHealthGateMs))
        {
            error("new version did not reach RUNNING within 30 s; rolling back");
            service.Stop(kRollbackStopMs);

            // Move the new binary out of the way first: a mapped image can be renamed while an exiting
            // process still holds it, but not replaced.
            const bool moved_out = Move(files.exe, files.new_exe);
            if (!Move(files.old_exe, files.exe))
            {
                error("rollback: cannot restore " + Narrow(files.exe) + " (error " + LastError() + ")");
                if (moved_out)
                    Move(files.new_exe, files.exe); // some binary is better than none
            }
            DeleteFileW(files.new_exe.c_str());
            start_again();
            return 1;
        }

        if (!ClearUpdateAttemptRecord(state_path))
            error("could not remove the update attempt record " + state_path.u8string());
        info("HSMAgent running with new version; update successful");
        return 0;
    }

    int RunApplyUpdate()
    {
        ScmServiceControl service;
        if (!service.Valid())
        {
            LogErr("cannot open the HSMAgent service (error " + LastError() + "); no update applied");
            return 1;
        }

        return ApplyStagedUpdate(CurrentUpdateFiles(), service, UpdateStatePath(),
                                 [](bool error, const std::string& message) {
                                     if (error)
                                         LogErr(message);
                                     else
                                         Log(message);
                                 });
    }

    // ---- --restart-service: bounce the service to apply a config change (no binary swap) ----------

    int RunRestartService()
    {
        SC_HANDLE scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (!scm)
        {
            LogErr("restart: cannot open SCM");
            return 1;
        }

        SC_HANDLE svc = OpenServiceW(scm, kServiceName, SERVICE_QUERY_STATUS | SERVICE_START);
        if (!svc)
        {
            CloseServiceHandle(scm);
            LogErr("restart: cannot open HSMAgent service");
            return 1;
        }

        // The caller RequestStop()s right after spawning us, so wait for the graceful shutdown to
        // finish before starting a fresh process (which re-reads config.json).
        Log("restart: waiting for HSMAgent to stop (up to 60 s)...");
        if (!WaitForServiceState(svc, SERVICE_STOPPED, 60000))
        {
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            LogErr("restart: HSMAgent did not stop within 60 s — leaving it as-is");
            return 1;
        }

        if (!StartServiceW(svc, 0, nullptr))
        {
            const DWORD err = GetLastError();
            // ALREADY_RUNNING: a second --restart-service helper (several groups toggled in one
            // response) already started it — that is success, not failure.
            if (err != ERROR_SERVICE_ALREADY_RUNNING)
            {
                CloseServiceHandle(svc);
                CloseServiceHandle(scm);
                LogErr("restart: StartService failed (" + std::to_string(err) + ") — service is stopped");
                return 1;
            }
        }

        Log("restart: HSMAgent restarted to apply a configuration change");
        CloseServiceHandle(svc);
        CloseServiceHandle(scm);
        return 0;
    }

    bool SpawnRestartService()
    {
        wchar_t exe_path[MAX_PATH] = {};
        if (!GetModuleFileNameW(nullptr, exe_path, MAX_PATH))
            return false;

        std::wstring cmd_line = std::wstring(L"\"") + exe_path + L"\" --restart-service";
        STARTUPINFOW si = {};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi = {};
        if (!CreateProcessW(nullptr, cmd_line.data(), nullptr, nullptr, FALSE,
                            DETACHED_PROCESS | CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
            return false;

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return true;
    }

} // namespace hsm::agent
