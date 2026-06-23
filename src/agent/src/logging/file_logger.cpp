#include "agent/file_logger.hpp"

#include <cstdio>
#include <ctime>
#include <utility>

namespace hsm::agent
{
    namespace
    {
        const char* LevelName(hsm::collector::LogLevel level)
        {
            switch (level)
            {
            case hsm::collector::LogLevel::Debug:
                return "DEBUG";
            case hsm::collector::LogLevel::Info:
                return "INFO";
            case hsm::collector::LogLevel::Error:
                return "ERROR";
            default:
                return "INFO";
            }
        }

        std::string Timestamp()
        {
            const std::time_t now = std::time(nullptr);
            std::tm tm_buf{};
#ifdef _WIN32
            localtime_s(&tm_buf, &now);
#else
            localtime_r(&now, &tm_buf);
#endif
            char buffer[32];
            const std::size_t length = std::strftime(buffer, sizeof(buffer), "%Y-%m-%d %H:%M:%S", &tm_buf);
            return std::string(buffer, length);
        }
    } // namespace

    FileLogger::FileLogger(std::wstring path, std::size_t max_bytes)
        : path_(std::move(path)), max_bytes_(max_bytes)
    {
        out_.open(path_, std::ios::app | std::ios::binary);
        if (out_)
        {
            out_.seekp(0, std::ios::end);
            written_ = static_cast<std::size_t>(out_.tellp());
        }
    }

    void FileLogger::RollIfNeeded(std::size_t incoming)
    {
        if (max_bytes_ == 0 || written_ + incoming <= max_bytes_)
            return;

        out_.close();
        std::wstring rolled = path_ + L".1";
        // Best-effort roll: replace any previous .1 and rotate the current file into it.
        _wremove(rolled.c_str());
        _wrename(path_.c_str(), rolled.c_str());
        out_.open(path_, std::ios::trunc | std::ios::binary);
        written_ = 0;
    }

    void FileLogger::Write(hsm::collector::LogLevel level, const std::string& message)
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!out_)
            return;

        std::string line = Timestamp();
        line += " [";
        line += LevelName(level);
        line += "] ";
        line += message;
        line += '\n';

        RollIfNeeded(line.size());
        if (!out_)
            return;

        out_.write(line.data(), static_cast<std::streamsize>(line.size()));
        out_.flush();
        written_ += line.size();
    }
} // namespace hsm::agent
