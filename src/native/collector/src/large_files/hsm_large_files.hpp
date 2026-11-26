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
        /// hsm_large_file_cancel: CANCELLED, unless the uploader committed STORED before.
        void Cancel(const std::string& why);
        /// The uploader has the server's `stored` and is about to delete the source (or keep it): from now
        /// on the outcome is STORED whatever a cancel says. False when a cancel came first: the source is
        /// then kept and the outcome stays CANCELLED.
        bool CommitStored();

    private:
        bool committed_ = false; // guarded by reason_mutex
    };

    /// Where the registration of a routed send's sensor stands on the session.
    enum class RoutedRegistration
    {
        Pending,
        Acknowledged,
        /// The server refused it (a 4xx or a per-path error): the value takes today's path at once.
        Refused,
    };

    /// What Route did with a routed send.
    enum class RouteResult
    {
        /// Queued for the uploader.
        Taken,
        /// Not routed: the caller takes today's path (a `list` frame).
        NotRouted,
        /// Dropped, said and counted: the routed sends waiting in memory reached their bound.
        Dropped,
    };

    /// An ordinary file send routed to the file connection (ADR-0022 point 20): the content of
    /// hsm_sensor_add_file and its kin larger than a quarter of the frame limit of the session it is sent
    /// on (decided when the value is about to be sent), from memory, never secure, nothing on a disk. Not
    /// sent: a dropped value, counted where a dropped value is.
    struct RoutedFile
    {
        std::uint64_t id = 0;
        std::string sensor; // the full registered path
        std::string name;   // name.extension
        std::shared_ptr<const std::string> content;
        /// The value's status (the wire's number) and comment; HSM_SENSOR_STATUS_OK and empty are not sent.
        int status = HSM_SENSOR_STATUS_OK;
        std::string comment;
        /// The value's time, 100 ns ticks since 1970 (begin's `modified`).
        std::int64_t time_ticks = 0;
        /// `send:<guid>`, made once per send.
        std::string source;
        /// Where the sensor's ordinary registration stands on the session.
        std::function<RoutedRegistration()> registration;
        /// Today's path (a `list` frame): the value back into the value queue, never routed again. Also
        /// called by a stop for a routed send no begin was answered for, before the value queue's drain.
        std::function<void(const RoutedFile&)> back_to_queue;
        /// When it was routed, on the collector's clock: a registration that is not acknowledged within a
        /// minute sends it back to today's path.
        std::int64_t routed_at_ms = 0;
        /// True once a begin was answered with an upload (a part exists on the server): a stop then drops
        /// it (said and counted) instead of giving it back to the value queue.
        bool begun = false;
        /// The bounds of its retries, kept across attempts.
        int discards = 0;
        int checksum_mismatches = 0;
        int protocol_closes = 0;
        /// Begins again after `end` answered 409 "pieces are missing", and 404s on a piece or at end.
        int no_progress = 0;
        int not_held = 0;
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
        /// Writes a line at debug level. Called with no lock held.
        std::function<void(const std::string& message)> debug;
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
        /// Queues a routed send. NotRouted when it cannot be routed now (not running, the route refused,
        /// the key refused): the caller takes today's path. Dropped (said and counted here) when the
        /// routed sends waiting in memory would pass 64 MiB; one is always taken when none waits.
        RouteResult Route(std::shared_ptr<RoutedFile> file);
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
        /// (Windows) and the thread's priority is raised. Then routed sends no begin was answered for go
        /// back to the value queue (before its stop drain); one whose upload had begun is dropped, said
        /// and counted. Idempotent.
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
        /// Runs on the uploader thread when a file was stored: "stored" before the outcome is committed,
        /// "deleted" after the source's delete (or keep) and before the outcome is settled.
        std::function<void(const std::string& phase)> on_finish_for_tests;
        bool UploaderAliveForTests() const;

    private:
        void MakeThread();

        struct Impl;
        std::unique_ptr<Impl> impl_;
        std::shared_ptr<LargeFileShared> shared_;
        std::atomic<std::uint64_t> next_id_{ 0 };
    };
} // namespace hsm::collector::large_files
