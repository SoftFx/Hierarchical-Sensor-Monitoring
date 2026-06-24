// HSM Agent — entry point. Dispatches on argv:
//   (no args) / --service   run as an SCM service (StartServiceCtrlDispatcher)
//   --console               run AgentRuntime in the foreground (Ctrl-C to stop) for debugging
//   --install / --uninstall register/remove the auto-start service (requires elevation)
//   --config <path>         override the config.json location (default %ProgramData%\HSM Agent)
//
// The single signed binary is identical across every product download; only the bundled config.json
// differs (epic #1167 signed-exe invariant).

#include "agent/agent_runtime.hpp"
#include "agent/config.hpp"
#include "agent/event_log.hpp"
#include "agent/file_logger.hpp"
#include "agent/logging.hpp"
#include "agent/paths.hpp"
#include "agent/service_install.hpp"
#include "agent/windows_service.hpp"

#include <atomic>
#include <iostream>
#include <string>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace
{
    // Read from the console-control-handler thread; atomic so a Ctrl-C during teardown never reads a
    // dangling pointer.
    std::atomic<hsm::agent::AgentRuntime*> g_console_runtime{ nullptr };

    BOOL WINAPI ConsoleCtrlHandler(DWORD type)
    {
        switch (type)
        {
        case CTRL_C_EVENT:
        case CTRL_BREAK_EVENT:
        case CTRL_CLOSE_EVENT:
        case CTRL_LOGOFF_EVENT:
        case CTRL_SHUTDOWN_EVENT:
            if (hsm::agent::AgentRuntime* runtime = g_console_runtime.load())
                runtime->RequestStop();
            return TRUE;
        default:
            return FALSE;
        }
    }

    void PrintUsage()
    {
        std::cout << "HSM Agent " HSM_AGENT_VERSION " — streams this computer's metrics to an HSM server.\n\n"
                     "Usage: hsm-agent [mode] [--config <path>]\n"
                     "  (no mode)      run as a Windows service (used by the SCM)\n"
                     "  --console      run in the foreground for debugging (Ctrl-C to stop)\n"
                     "  --install      register the auto-start service (requires elevation)\n"
                     "  --uninstall    remove the service (requires elevation)\n"
                     "  --config <p>   use config file <p> (default: %ProgramData%\\HSM Agent\\config.json)\n"
                     "  --version      print version and exit\n";
    }

    int RunConsole(const std::wstring& config_path)
    {
        using namespace hsm::agent;

        EnsureDirectories();

        std::string text;
        if (!ReadTextFileWide(config_path, text))
        {
            std::wcerr << L"hsm-agent: cannot read config file: " << config_path << L"\n";
            return 1;
        }

        AgentConfig config;
        std::string error;
        if (!ParseAgentConfig(text, config, error))
        {
            std::cerr << "hsm-agent: " << error << '\n';
            return 1;
        }

        FileLogger file(LogFilePath());
        EventLogSink event_log;
        auto logger = MakeAgentLogger(&file, &event_log, /*also_stderr=*/true);

        AgentRuntime runtime(std::move(config), std::move(logger));
        g_console_runtime.store(&runtime);
        SetConsoleCtrlHandler(ConsoleCtrlHandler, TRUE);

        std::cout << "hsm-agent: running in console mode (Ctrl-C to stop).\n";
        const int rc = runtime.Run();
        g_console_runtime.store(nullptr);
        return rc;
    }
} // namespace

int wmain(int argc, wchar_t** argv)
{
    using namespace hsm::agent;

    std::wstring mode;
    std::wstring config_path = DefaultConfigPath();

    for (int i = 1; i < argc; ++i)
    {
        const std::wstring arg = argv[i];
        if (arg == L"--install" || arg == L"--uninstall" || arg == L"--console" || arg == L"--service")
        {
            mode = arg;
        }
        else if (arg == L"--config" && i + 1 < argc)
        {
            config_path = argv[++i];
        }
        else if (arg == L"--version" || arg == L"-v")
        {
            std::cout << "hsm-agent " << HSM_AGENT_VERSION << "\n";
            return 0;
        }
        else if (arg == L"--help" || arg == L"-h" || arg == L"/?")
        {
            PrintUsage();
            return 0;
        }
        else
        {
            std::wcerr << L"hsm-agent: unknown argument: " << arg << L"\n";
            PrintUsage();
            return 2;
        }
    }

    if (mode == L"--install")
        return InstallService();
    if (mode == L"--uninstall")
        return UninstallService();

    // Console + service runs must be single-instance so they never double-send. A Global mutex may
    // fail for a non-elevated user (no SeCreateGlobalPrivilege); in that case skip the guard rather
    // than block a legitimate console debug run.
    HANDLE mutex = CreateMutexW(nullptr, FALSE, kSingleInstanceMutex);
    if (mutex != nullptr && GetLastError() == ERROR_ALREADY_EXISTS)
    {
        std::cerr << "hsm-agent: another instance is already running.\n";
        CloseHandle(mutex);
        return 3;
    }

    const int rc = (mode == L"--console") ? RunConsole(config_path) : RunService(config_path);

    // Single-instance relies on the named mutex's existence, not ownership (CreateMutexW was called
    // with bInitialOwner=FALSE), so just close the handle — the mutex goes away with the last handle.
    if (mutex != nullptr)
        CloseHandle(mutex);
    return rc;
}
