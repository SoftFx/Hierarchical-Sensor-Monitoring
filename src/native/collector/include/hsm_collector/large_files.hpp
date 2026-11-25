#pragma once

/// @file
/// @brief Large files in the C++ wrapper (since 0.16.0, #1887, ADR-0022): a thin RAII layer over the
/// hsm_large_file_* functions of the C ABI. Watch a folder with Collector::WatchLargeFiles, hand one file
/// over with Collector::SendLargeFile.

#include "hsm_collector/error.hpp"
#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/options.hpp"

#include <chrono>
#include <cstdint>
#include <optional>
#include <string>
#include <utility>

namespace hsm::collector
{
    /// The options of a watch or a handed-over file. Every default is the C ABI's.
    struct LargeFileOptions
    {
        /// How long the size and modification time must stay unchanged before a watched file is taken
        /// (60 s by default, at least 1 s).
        std::chrono::milliseconds settle{ 60000 };
        /// False: the source is deleted after the server stored it. True: kept and not sent again.
        bool keep_source = false;
        /// Bytes sent per second, kept by waiting between pieces (10,000,000 by default); 0: no limit.
        std::int64_t max_bytes_per_second = 10000000;
        /// The File sensor is registered with the alert "new data has been received".
        bool new_file_alert = true;
        SensorLocation location = SensorLocation::Module;
        std::string description;
        /// Zero: the server's default.
        std::chrono::milliseconds keep_history{ 0 };
        std::chrono::milliseconds ttl{ 0 };
        std::chrono::milliseconds self_destroy{ 0 };
    };

    enum class LargeFileWatchState
    {
        Active = HSM_LARGE_FILE_WATCH_ACTIVE,
        Unavailable = HSM_LARGE_FILE_WATCH_UNAVAILABLE,
        Refused = HSM_LARGE_FILE_WATCH_REFUSED,
        Stopped = HSM_LARGE_FILE_WATCH_STOPPED,
    };

    enum class LargeFileOutcome
    {
        Pending = HSM_LARGE_FILE_PENDING,
        Stored = HSM_LARGE_FILE_STORED,
        Refused = HSM_LARGE_FILE_REFUSED,
        GivenUp = HSM_LARGE_FILE_GIVEN_UP,
        Missing = HSM_LARGE_FILE_MISSING,
        Unavailable = HSM_LARGE_FILE_UNAVAILABLE,
        Cancelled = HSM_LARGE_FILE_CANCELLED,
    };

    namespace detail
    {
        inline hsm_large_file_options_t ToNative(const LargeFileOptions& options)
        {
            hsm_large_file_options_t native = hsm_large_file_options_default();
            native.settle_ms = static_cast<int64_t>(options.settle.count());
            native.keep_source = options.keep_source ? 1 : 0;
            native.max_bytes_per_second = options.max_bytes_per_second == 0 ? HSM_LARGE_FILE_NO_SPEED_LIMIT : options.max_bytes_per_second;
            native.no_new_file_alert = options.new_file_alert ? 0 : 1;
            native.sensor_location = static_cast<int32_t>(options.location);
            native.description = options.description.empty() ? nullptr : options.description.c_str();
            native.keep_history_ms = static_cast<int64_t>(options.keep_history.count());
            native.ttl_ms = static_cast<int64_t>(options.ttl.count());
            native.self_destroy_ms = static_cast<int64_t>(options.self_destroy.count());
            return native;
        }

        template <class Handle>
        std::string ReadReason(size_t (*read)(const Handle*, char*, size_t), const Handle* handle)
        {
            const size_t length = read(handle, nullptr, 0);
            std::string text(length + 1, '\0');
            read(handle, text.data(), text.size());
            text.resize(length);
            return text;
        }
    } // namespace detail

    /// A watched folder. Move-only; destroying it (or Release) stops watching and cuts its upload in
    /// progress, whose part stays on the server for a continue. Valid after its collector is destroyed.
    class LargeFileWatch
    {
    public:
        LargeFileWatch() = default;
        explicit LargeFileWatch(hsm_large_file_watch_t* handle) noexcept
            : handle_(handle)
        {
        }
        LargeFileWatch(LargeFileWatch&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr))
        {
        }
        LargeFileWatch& operator=(LargeFileWatch&& other) noexcept
        {
            if (this != &other)
            {
                Release();
                handle_ = std::exchange(other.handle_, nullptr);
            }
            return *this;
        }
        LargeFileWatch(const LargeFileWatch&) = delete;
        LargeFileWatch& operator=(const LargeFileWatch&) = delete;
        ~LargeFileWatch() { Release(); }

        void Release() noexcept
        {
            hsm_large_file_watch_release(handle_);
            handle_ = nullptr;
        }

        LargeFileWatchState State() const { return static_cast<LargeFileWatchState>(Stats().state); }
        std::uint64_t FilesStored() const { return Stats().files_stored; }
        std::uint64_t FilesNotSent() const { return Stats().files_not_sent; }
        /// Why the watch is Unavailable or Refused, or the last problem said ("" when none).
        std::string Reason() const { return handle_ == nullptr ? std::string() : detail::ReadReason(hsm_large_file_watch_state_reason, handle_); }

        hsm_large_file_watch_t* Handle() const noexcept { return handle_; }

    private:
        hsm_large_file_watch_stats_t Stats() const
        {
            hsm_large_file_watch_stats_t stats{};
            stats.struct_size = sizeof stats;
            stats.state = HSM_LARGE_FILE_WATCH_STOPPED;
            if (handle_ != nullptr)
                detail::ThrowIfFailed(hsm_large_file_watch_get_stats(handle_, &stats), "Failed to read the large file watch");
            return stats;
        }

        hsm_large_file_watch_t* handle_ = nullptr;
    };

    /// One handed-over file. Move-only; destroying the handle leaves the file queued.
    class LargeFile
    {
    public:
        LargeFile() = default;
        explicit LargeFile(hsm_large_file_t* handle) noexcept
            : handle_(handle)
        {
        }
        LargeFile(LargeFile&& other) noexcept
            : handle_(std::exchange(other.handle_, nullptr))
        {
        }
        LargeFile& operator=(LargeFile&& other) noexcept
        {
            if (this != &other)
            {
                hsm_large_file_release(handle_);
                handle_ = std::exchange(other.handle_, nullptr);
            }
            return *this;
        }
        LargeFile(const LargeFile&) = delete;
        LargeFile& operator=(const LargeFile&) = delete;
        ~LargeFile() { hsm_large_file_release(handle_); }

        LargeFileOutcome Outcome() const { return static_cast<LargeFileOutcome>(Result().outcome); }
        bool SourceDeleted() const { return Result().source_deleted; }
        /// The value's time the server answered; empty unless Stored (and when the server had the file before).
        std::optional<std::chrono::system_clock::time_point> StoredTime() const
        {
            const auto result = Result();
            if (result.outcome != HSM_LARGE_FILE_STORED || result.stored_time_unix_ns == 0)
                return std::nullopt;
            return std::chrono::system_clock::time_point(std::chrono::duration_cast<std::chrono::system_clock::duration>(std::chrono::nanoseconds(result.stored_time_unix_ns)));
        }
        /// Why the outcome is not Stored ("" while Pending and when Stored).
        std::string Reason() const { return handle_ == nullptr ? std::string() : detail::ReadReason(hsm_large_file_reason, handle_); }
        /// Takes the file out of the queue; an upload in progress is cut.
        void Cancel() noexcept { hsm_large_file_cancel(handle_); }

        hsm_large_file_t* Handle() const noexcept { return handle_; }

    private:
        hsm_large_file_result_t Result() const
        {
            hsm_large_file_result_t result{};
            result.struct_size = sizeof result;
            result.outcome = HSM_LARGE_FILE_CANCELLED;
            if (handle_ != nullptr)
                detail::ThrowIfFailed(hsm_large_file_get_result(handle_, &result), "Failed to read the large file");
            return result;
        }

        hsm_large_file_t* handle_ = nullptr;
    };
} // namespace hsm::collector
