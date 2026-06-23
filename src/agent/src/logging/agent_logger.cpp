#include "agent/logging.hpp"

#include <iostream>

namespace hsm::agent
{
    AgentRuntime::LogFn MakeAgentLogger(FileLogger* file, EventLogSink* event_log, bool also_stderr)
    {
        return [file, event_log, also_stderr](hsm::collector::LogLevel level, const std::string& message) {
            if (file != nullptr)
                file->Write(level, message);
            if (event_log != nullptr)
                event_log->Write(level, message);
            if (also_stderr)
                std::cerr << "[hsm-agent] (" << static_cast<int>(level) << ") " << message << '\n';
        };
    }
} // namespace hsm::agent
