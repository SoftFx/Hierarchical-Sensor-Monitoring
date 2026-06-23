#include "agent/windows_service.hpp"

#include "agent/agent_runtime.hpp"
#include "agent/config.hpp"
#include "agent/event_log.hpp"
#include "agent/file_logger.hpp"
#include "agent/logging.hpp"
#include "agent/paths.hpp"
#include "agent/service_install.hpp"

#include <atomic>
#include <iostream>
#include <string>
#include <thread>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace hsm::agent
{
    namespace
    {
        SERVICE_STATUS_HANDLE g_status_handle = nullptr;
        SERVICE_STATUS g_status{};
        DWORD g_checkpoint = 0;
        // Shared between the SCM control-handler thread and ServiceMain — atomic so the handler never
        // reads a torn/dangling pointer while ServiceMain sets or clears it.
        std::atomic<AgentRuntime*> g_runtime{ nullptr };
        std::wstring g_config_path;

        void SetState(DWORD state, DWORD exit_code = NO_ERROR, DWORD wait_hint = 0)
        {
            g_status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
            g_status.dwCurrentState = state;
            g_status.dwWin32ExitCode = exit_code;
            g_status.dwWaitHint = wait_hint;

            g_status.dwControlsAccepted =
                (state == SERVICE_RUNNING) ? (SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN) : 0u;

            if (state == SERVICE_RUNNING || state == SERVICE_STOPPED)
                g_status.dwCheckPoint = 0;
            else
                g_status.dwCheckPoint = ++g_checkpoint;

            if (g_status_handle != nullptr)
                SetServiceStatus(g_status_handle, &g_status);
        }

        DWORD WINAPI HandlerEx(DWORD control, DWORD /*event_type*/, LPVOID /*event_data*/, LPVOID /*context*/)
        {
            switch (control)
            {
            case SERVICE_CONTROL_STOP:
            case SERVICE_CONTROL_SHUTDOWN:
            case SERVICE_CONTROL_PRESHUTDOWN:
                SetState(SERVICE_STOP_PENDING, NO_ERROR, 8000);
                if (AgentRuntime* runtime = g_runtime.load())
                    runtime->RequestStop();
                return NO_ERROR;
            case SERVICE_CONTROL_INTERROGATE:
                return NO_ERROR;
            default:
                return ERROR_CALL_NOT_IMPLEMENTED;
            }
        }

        void WINAPI ServiceMain(DWORD /*argc*/, LPWSTR* /*argv*/)
        {
            g_status_handle = RegisterServiceCtrlHandlerExW(kServiceName, HandlerEx, nullptr);
            if (g_status_handle == nullptr)
                return;

            SetState(SERVICE_START_PENDING, NO_ERROR, 5000);

            EnsureDirectories();
            FileLogger file(LogFilePath());
            EventLogSink event_log;

            std::string text;
            if (!ReadTextFileWide(g_config_path, text))
            {
                file.Write(hsm::collector::LogLevel::Error, "cannot read config file");
                event_log.ReportError("HSM Agent failed to start: cannot read config file.");
                SetState(SERVICE_STOPPED, ERROR_FILE_NOT_FOUND);
                return;
            }

            AgentConfig config;
            std::string error;
            if (!ParseAgentConfig(text, config, error))
            {
                file.Write(hsm::collector::LogLevel::Error, "config error: " + error);
                event_log.ReportError("HSM Agent failed to start: " + error);
                SetState(SERVICE_STOPPED, ERROR_BAD_CONFIGURATION);
                return;
            }

            auto logger = MakeAgentLogger(&file, &event_log, false);
            AgentRuntime runtime(std::move(config), std::move(logger));
            g_runtime.store(&runtime);
            event_log.ReportInformation("HSM Agent service starting.");

            int exit_code = 0;
            std::thread worker([&runtime, &exit_code] {
                exit_code = runtime.Run([] { SetState(SERVICE_RUNNING); });
            });
            worker.join();

            g_runtime.store(nullptr);
            if (exit_code == 0)
            {
                event_log.ReportInformation("HSM Agent service stopped.");
                SetState(SERVICE_STOPPED, NO_ERROR);
            }
            else
            {
                event_log.ReportError("HSM Agent service exited with a fatal error.");
                SetState(SERVICE_STOPPED, ERROR_PROCESS_ABORTED);
            }
        }
    } // namespace

    int RunService(const std::wstring& config_path)
    {
        g_config_path = config_path;

        SERVICE_TABLE_ENTRYW table[] = {
            { const_cast<LPWSTR>(kServiceName), &ServiceMain },
            { nullptr, nullptr },
        };

        if (!StartServiceCtrlDispatcherW(table))
        {
            const DWORD code = GetLastError();
            if (code == ERROR_FAILED_SERVICE_CONTROLLER_CONNECT)
            {
                std::cerr << "hsm-agent: not started by the service control manager. "
                             "Use --console for a foreground run, or --install to register the service.\n";
                return 2;
            }
            std::cerr << "hsm-agent: service dispatcher failed (error " << code << ")\n";
            return 1;
        }
        return 0;
    }
} // namespace hsm::agent
