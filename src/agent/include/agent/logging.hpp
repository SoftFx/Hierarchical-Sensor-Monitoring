#pragma once

/// @file
/// @brief Fan-out logger: composes the file sink, the Event Log sink, and (optionally) stderr into
/// the single `AgentRuntime::LogFn` the collector calls.

#include "agent/agent_runtime.hpp"
#include "agent/event_log.hpp"
#include "agent/file_logger.hpp"

namespace hsm::agent
{
    /// Build the agent log callback. `file` receives every line; `event_log` (if non-null) receives
    /// Error-level lines; `also_stderr` additionally mirrors to stderr (console mode). The returned
    /// callable captures the raw pointers, so both sinks must outlive the AgentRuntime.
    AgentRuntime::LogFn MakeAgentLogger(FileLogger* file, EventLogSink* event_log, bool also_stderr);
} // namespace hsm::agent
