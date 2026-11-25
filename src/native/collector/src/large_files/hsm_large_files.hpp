#pragma once

/// @file
/// @brief Large file values of one native collector (#1887, ADR-0022): watched folders, handed-over files
/// and the one uploader thread that takes settled files and uploads them in pieces on the file connection.
/// The managed counterpart is HSMDataCollector's large file uploader (#1888); the observable rules are the
/// same (aicontext/features/collector/large-files/feature.md).
///
/// Threads. The application adds watches and files on any thread; handles read their state on any thread.
/// One uploader thread, made at Start (or at the first watch or file while running) and joined at Stop,
/// does everything else: scans, reads, compresses, hashes, sends, writes the state. It runs at lowered
/// priority. Stop cuts the upload in progress (the part stays on the server for a continue) and joins.
///
/// Lifetime. The collector owns LargeFiles. A handle holds its WatchState or FileState, which hold only
/// LargeFileShared (a mutex, a condition variable, the cut of the upload in progress): never LargeFiles
/// nor the collector, so an application thread can never become the collector's last owner, and a handle
/// outlives its collector safely. The RunContext's functions call back into the collector; they are used
/// only by the uploader thread, which Stop joins before the collector goes.

#include "hsm_collector/hsm_collector.h"

#if defined(HSM_COLLECTOR_LARGE_FILES)
#include "../hsm_session_transport.hpp" // SessionIdentity, SessionResult
#include "hsm_file_connection.hpp"
#endif

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include <vector>

namespace hsm::collector::large_files
{
    /// The options of a watch or a file with every default applied.
    struct ResolvedOptions
    {
        std::int64_t settle_ms = 60000;
        bool keep_source = false;
        /// Bytes sent per second; 0: no limit.
        std::int64_t max_bytes_per_second = 10000000;
        bool new_file_alert = true;
        /// Kept under the product's dump password on the server.
        bool secure = true;
    };

    /// What handles and the uploader share: the wake-up and the cut of the upload in progress.
    class LargeFileShared
    {
    public:
        void Wake();
        /// Cuts the upload of `owner` (a watch or file id) when it is the one in progress.
        void Cut(std::uint64_t owner);

        std::mutex mutex;
        std::condition_variable cv;
        bool disposed = false;
        std::uint64_t wakes = 0;
        /// The owner of the upload in progress (0: none) and how to cut it. Set by the uploader.
        std::uint64_t current_owner = 0;
        std::function<void()> cut;
    };

    struct WatchState
    {
        std::uint64_t id = 0;
        std::shared_ptr<LargeFileShared> shared;
        std::string folder; // absolute
        std::string mask;
        std::string sensor;            // the full registered path
        std::string registration_json; // the AddOrUpdate of the File sensor
        std::string data_folder;
        ResolvedOptions options;

        std::atomic<int> state{ HSM_LARGE_FILE_WATCH_ACTIVE };
        std::atomic<std::uint64_t> files_stored{ 0 };
        std::atomic<std::uint64_t> files_not_sent{ 0 };
        std::atomic<bool> released{ false };
        mutable std::mutex reason_mutex;
        std::string reason;

        void SetReason(const std::string& text);
        std::string Reason() const;
    };

    struct FileState
    {
        std::uint64_t id = 0;
        std::shared_ptr<LargeFileShared> shared;
        std::string path; // absolute
        std::string sensor;
        std::string registration_json;
        std::string data_folder;
        ResolvedOptions options;

        std::atomic<int> outcome{ HSM_LARGE_FILE_PENDING };
        std::atomic<bool> cancelled{ false };
        std::atomic<bool> source_deleted{ false };
        std::atomic<std::int64_t> stored_ticks{ 0 };
        mutable std::mutex reason_mutex;
        std::string reason;

        /// Settles the outcome once; false when it was settled before.
        bool Settle(hsm_large_file_outcome_t outcome, const std::string& why);
        std::string Reason() const;
    };

    /// An ordinary file send routed to the file connection (ADR-0022 point 20): the content of
    /// hsm_sensor_add_file and its kin larger than a quarter of the session's frame limit, from memory,
    /// never secure, nothing on a disk. Not sent: a dropped value, counted where a dropped value is.
    struct RoutedFile
    {
        std::uint64_t id = 0;
        std::string sensor; // the full registered path
        std::string name;   // name.extension
        std::string content;
        /// The value's status (the wire's number) and comment; HSM_SENSOR_STATUS_OK and empty are not sent.
        int status = HSM_SENSOR_STATUS_OK;
        std::string comment;
        /// The value's time, 100 ns ticks since 1970 (begin's `modified`).
        std::int64_t time_ticks = 0;
        /// `send:<guid>`, made once per send.
        std::string source;
        /// True once the sensor's ordinary registration was acknowledged on the session.
        std::function<bool()> registered;
        /// Today's path (a `list` frame), when the file connection turns out to be missing; the JSON of
        /// the value is built from the file only then.
        std::function<void(const RoutedFile&)> back_to_queue;
        /// When it was routed (the real steady clock): a registration that is not acknowledged within a
        /// minute sends it back to today's path.
        std::chrono::steady_clock::time_point routed_at{};
        /// The bounds of its retries, kept across attempts.
        int discards = 0;
        int checksum_mismatches = 0;
        int protocol_closes = 0;
    };

#if defined(HSM_COLLECTOR_LARGE_FILES)
    /// What one run of the uploader needs from its collector (between Start and Stop).
    struct RunContext
    {
        /// The collector's monotonic clock in ms (the tests' manual clock).
        std::function<std::int64_t()> now_ms;
        /// Writes a line: `error` true for the deduplicated error channel. Called with no lock held.
        std::function<void(bool error, const std::string& message)> report;
        /// One file not sent, for the Queue overflow sensor.
        std::function<void()> count_lost_file;
        /// One routed send not sent: a dropped value (the collector's overflow counter).
        std::function<void()> count_dropped_value;
        /// The session that is up now.
        std::function<hsm::http::SessionIdentity()> session;
        /// A `commands` request on the session (the registration of a File sensor); the wait ends once
        /// `abandon` reads true after `wake_session`.
        std::function<hsm::http::SessionResult(const std::string& body, const std::atomic<bool>& abandon)> commands;
        /// Wakes the session's waiting requests (a stop).
        std::function<void()> wake_session;
        FileConnectionOptions connection;
        /// Not empty: this run cannot send large files (said once, every watch and file UNAVAILABLE).
        std::string unavailable_reason;
    };
#endif

    class LargeFiles
    {
    public:
        LargeFiles();
        ~LargeFiles();

        LargeFiles(const LargeFiles&) = delete;
        LargeFiles& operator=(const LargeFiles&) = delete;

        /// Adds a watch; false, with `error`, for an equal live watch.
        bool AddWatch(const std::shared_ptr<WatchState>& watch, std::string& error);
        void AddFile(const std::shared_ptr<FileState>& file);
        /// Queues a routed send; false when it cannot be routed now (not running, no route): the caller
        /// takes today's path.
        bool Route(std::shared_ptr<RoutedFile> file);
        /// False when the file connection was refused (404 or 400) on the session of `generation`.
        bool RouteAvailable(std::uint64_t generation) const noexcept;
        /// A new state shared with the handles of this collector.
        const std::shared_ptr<LargeFileShared>& Shared() const noexcept { return shared_; }
        std::uint64_t NextId() noexcept { return ++next_id_; }
        /// An equal pending file (the same path and sensor) to hand out again, or null.
        std::shared_ptr<FileState> FindPendingFile(const std::string& path, const std::string& sensor, bool secure);

        /// Says why this collector cannot send large files at all (a build without them): every watch
        /// and file is UNAVAILABLE at once.
        /// Where lines go while no run has a context (before the first Start, after a stop).
        void SetReporter(std::function<void(bool error, const std::string& message)> report);
        void SetBuildUnavailable(const std::string& reason);
        /// The transport the collector chose: not empty, why it cannot carry large files (the HTTP transport);
        /// empty, it may (the session). Watches and pending files follow at once; the run decides again at Start.
        void SetTransportUnavailable(const std::string& reason);

#if defined(HSM_COLLECTOR_LARGE_FILES)
        /// Starts a run: the uploader thread when there is anything to upload.
        void Start(RunContext context);
#endif
        /// Cuts the upload in progress and joins the uploader. Bounded: blocking reads are cancelled
        /// (Windows) and the thread's priority is raised. Idempotent.
        void Stop() noexcept;
        /// Stop, then every handed-over file not settled becomes CANCELLED (counted and said) and every
        /// watch STOPPED. Idempotent.
        void Dispose(const std::function<void(bool, const std::string&)>& report, const std::function<void()>& count_lost) noexcept;
        bool IsUploaderThread() const noexcept;

        // ---- Tests ----
        /// The next loop of the uploader scans every watch now, whatever its interval.
        void ScanNowForTests();
        /// Runs on the uploader thread for every wait it starts: what it waits for and for how long (ms).
        std::function<void(const std::string& what, std::int64_t ms)> on_wait_for_tests;
        bool UploaderAliveForTests() const;

    private:
        void MakeThread();

        struct Impl;
        std::unique_ptr<Impl> impl_;
        std::shared_ptr<LargeFileShared> shared_;
        std::atomic<std::uint64_t> next_id_{ 0 };
    };
} // namespace hsm::collector::large_files
