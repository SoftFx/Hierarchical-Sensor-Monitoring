#include "agent/service_install.hpp"

#include "agent/event_log.hpp"

#include <iostream>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace hsm::agent
{
    namespace
    {
        void ReportLastError(const char* context)
        {
            const DWORD code = GetLastError();
            std::cerr << "hsm-agent: " << context << " failed (error " << code << ")\n";
        }

        // Closing helpers keep the install/uninstall flow free of nested cleanup branches.
        struct ScmHandle
        {
            SC_HANDLE handle = nullptr;
            ~ScmHandle()
            {
                if (handle != nullptr)
                    CloseServiceHandle(handle);
            }
        };
    } // namespace

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
            path.resize(path.size() * 2); // truncated → grow and retry
        }
    }

    bool IsElevated()
    {
        HANDLE token = nullptr;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
            return false;

        TOKEN_ELEVATION elevation{};
        DWORD size = sizeof(elevation);
        const bool ok = GetTokenInformation(token, TokenElevation, &elevation, sizeof(elevation), &size) != FALSE;
        CloseHandle(token);
        return ok && elevation.TokenIsElevated != 0;
    }

    int InstallService()
    {
        if (!IsElevated())
        {
            std::cerr << "hsm-agent: --install requires elevation (run as Administrator)\n";
            return 1;
        }

        const std::wstring exe = CurrentExePath();
        if (exe.empty())
        {
            ReportLastError("resolving executable path");
            return 1;
        }
        const std::wstring quoted = L"\"" + exe + L"\"";

        ScmHandle scm;
        scm.handle = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CREATE_SERVICE);
        if (scm.handle == nullptr)
        {
            ReportLastError("opening the service control manager");
            return 1;
        }

        ScmHandle service;
        service.handle = CreateServiceW(
            scm.handle,
            kServiceName,
            kServiceDisplayName,
            SERVICE_ALL_ACCESS,
            SERVICE_WIN32_OWN_PROCESS,
            SERVICE_AUTO_START,
            SERVICE_ERROR_NORMAL,
            quoted.c_str(),
            nullptr,
            nullptr,
            nullptr,
            nullptr,
            nullptr);

        if (service.handle == nullptr)
        {
            if (GetLastError() != ERROR_SERVICE_EXISTS)
            {
                ReportLastError("creating the service");
                return 1;
            }
            // Already present: reopen and bring its config back to the intended state (idempotent).
            service.handle = OpenServiceW(scm.handle, kServiceName, SERVICE_ALL_ACCESS);
            if (service.handle == nullptr)
            {
                ReportLastError("opening the existing service");
                return 1;
            }
            if (!ChangeServiceConfigW(
                    service.handle,
                    SERVICE_WIN32_OWN_PROCESS,
                    SERVICE_AUTO_START,
                    SERVICE_ERROR_NORMAL,
                    quoted.c_str(),
                    nullptr,
                    nullptr,
                    nullptr,
                    nullptr,
                    nullptr,
                    kServiceDisplayName))
            {
                ReportLastError("reconfiguring the existing service");
                return 1;
            }
        }

        // Delayed auto-start so the agent does not contend with boot-critical services.
        SERVICE_DELAYED_AUTO_START_INFO delayed{};
        delayed.fDelayedAutostart = TRUE;
        ChangeServiceConfig2W(service.handle, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, &delayed);

        // Auto-restart on crash: restart after 60s for the first three failures, reset the counter
        // after a day.
        SC_ACTION actions[3];
        for (auto& action : actions)
        {
            action.Type = SC_ACTION_RESTART;
            action.Delay = 60000;
        }
        SERVICE_FAILURE_ACTIONS failure{};
        failure.dwResetPeriod = 24 * 60 * 60;
        failure.lpRebootMsg = nullptr;
        failure.lpCommand = nullptr;
        failure.cActions = 3;
        failure.lpsaActions = actions;
        ChangeServiceConfig2W(service.handle, SERVICE_CONFIG_FAILURE_ACTIONS, &failure);

        std::wstring description = L"Streams this computer's metrics to an HSM server.";
        SERVICE_DESCRIPTIONW desc{};
        desc.lpDescription = description.data();
        ChangeServiceConfig2W(service.handle, SERVICE_CONFIG_DESCRIPTION, &desc);

        if (!RegisterEventSourceKey(exe))
            std::cerr << "hsm-agent: warning: could not register the Event Log source\n";

        std::cout << "hsm-agent: service '" << "HSMAgent" << "' installed (auto-start, delayed).\n";
        return 0;
    }

    int UninstallService()
    {
        if (!IsElevated())
        {
            std::cerr << "hsm-agent: --uninstall requires elevation (run as Administrator)\n";
            return 1;
        }

        ScmHandle scm;
        scm.handle = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_CONNECT);
        if (scm.handle == nullptr)
        {
            ReportLastError("opening the service control manager");
            return 1;
        }

        ScmHandle service;
        service.handle = OpenServiceW(scm.handle, kServiceName, SERVICE_STOP | SERVICE_QUERY_STATUS | DELETE);
        if (service.handle == nullptr)
        {
            if (GetLastError() == ERROR_SERVICE_DOES_NOT_EXIST)
            {
                UnregisterEventSourceKey();
                std::cout << "hsm-agent: service was not installed.\n";
                return 0;
            }
            ReportLastError("opening the service");
            return 1;
        }

        SERVICE_STATUS status{};
        if (QueryServiceStatus(service.handle, &status) && status.dwCurrentState != SERVICE_STOPPED)
        {
            ControlService(service.handle, SERVICE_CONTROL_STOP, &status);
            // Wait briefly for the stop to take effect before deleting.
            for (int i = 0; i < 50 && status.dwCurrentState != SERVICE_STOPPED; ++i)
            {
                Sleep(200);
                if (!QueryServiceStatus(service.handle, &status))
                    break;
            }
        }

        if (!DeleteService(service.handle))
        {
            const DWORD code = GetLastError();
            if (code != ERROR_SERVICE_MARKED_FOR_DELETE)
            {
                ReportLastError("deleting the service");
                return 1;
            }
        }

        UnregisterEventSourceKey();
        std::cout << "hsm-agent: service 'HSMAgent' uninstalled.\n";
        return 0;
    }
} // namespace hsm::agent
