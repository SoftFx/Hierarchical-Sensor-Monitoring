#pragma once

/// @file
/// @brief Thread-safe rolling file log sink for the agent (everything → %ProgramData%\HSM Agent\logs).

#include <hsm_collector/enums.hpp>

#include <cstddef>
#include <fstream>
#include <mutex>
#include <string>

namespace hsm::agent
{
    /// Appends timestamped lines to a log file, rolling to `<path>.1` when it exceeds `max_bytes`.
    /// Safe to call `Write` from any thread (the collector logs on its scheduler thread).
    class FileLogger
    {
    public:
        /// Messages below `min_level` are dropped (default Info — Debug stays out of the file unless
        /// explicitly lowered; parity with the collector's built-in file logger).
        explicit FileLogger(std::wstring path,
                            hsm::collector::LogLevel min_level = hsm::collector::LogLevel::Info,
                            std::size_t max_bytes = 5u * 1024u * 1024u);

        FileLogger(const FileLogger&) = delete;
        FileLogger& operator=(const FileLogger&) = delete;

        void Write(hsm::collector::LogLevel level, const std::string& message);

    private:
        void RollIfNeeded(std::size_t incoming);

        std::mutex mutex_;
        std::wstring path_;
        std::size_t max_bytes_;
        hsm::collector::LogLevel min_level_;
        std::ofstream out_;
        std::size_t written_ = 0;
    };
} // namespace hsm::agent
