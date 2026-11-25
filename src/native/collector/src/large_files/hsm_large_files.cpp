#include "hsm_large_files.hpp"

#if defined(HSM_COLLECTOR_LARGE_FILES)
#include "../tables/hsm_zstd.hpp"
#include "hsm_large_file_mask.hpp"
#include "hsm_large_file_platform.hpp"
#include "hsm_large_file_state.hpp"
#include "hsm_sha256.hpp"

#include <nlohmann/json.hpp>
#include <zstd.h>
#endif

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <utility>

namespace hsm::collector::large_files
{
    // ---- Shared state ----

    void LargeFileShared::Wake()
    {
        {
            std::lock_guard<std::mutex> guard(mutex);
            ++wakes;
        }
        cv.notify_all();
    }

    void LargeFileShared::Cut(std::uint64_t owner)
    {
        std::function<void()> cut_now;
        {
            std::lock_guard<std::mutex> guard(mutex);
            ++wakes;
            if (owner != 0 && owner == current_owner)
                cut_now = cut;
        }
        if (cut_now)
            cut_now();
        cv.notify_all();
    }

    void WatchState::SetReason(const std::string& text)
    {
        std::lock_guard<std::mutex> guard(reason_mutex);
        reason = text;
    }

    std::string WatchState::Reason() const
    {
        std::lock_guard<std::mutex> guard(reason_mutex);
        return reason;
    }

    bool FileState::Settle(hsm_large_file_outcome_t settled, const std::string& why)
    {
        std::lock_guard<std::mutex> guard(reason_mutex);
        int pending = HSM_LARGE_FILE_PENDING;
        if (!outcome.compare_exchange_strong(pending, settled))
            return false;
        reason = why;
        return true;
    }

    std::string FileState::Reason() const
    {
        std::lock_guard<std::mutex> guard(reason_mutex);
        return reason;
    }

#if defined(HSM_COLLECTOR_LARGE_FILES)
    namespace
    {
        constexpr std::int64_t kMinScanMs = 250;
        constexpr std::int64_t kMaxScanMs = 10000;
        constexpr std::int64_t kSliceMs = 50;
        constexpr std::int64_t kFeatureAbsentRetryMs = 30 * 60 * 1000;
        constexpr std::int64_t kMaxBackoffMs = 60000;
        constexpr std::int64_t kDefaultRetryMs = 5000;
        constexpr int kMaxDiscards = 3;
        constexpr int kMaxChecksumMismatches = 10;
        constexpr int kChecksumMismatchesInARow = 3;
        constexpr int kMaxProtocolCloses = 3;
        constexpr int kHeldOpenReportAfter = 10;

        std::int64_t ScanIntervalMs(std::int64_t settle_ms)
        {
            return (std::max)(kMinScanMs, (std::min)(settle_ms / 4, kMaxScanMs));
        }

        std::string Status(const FileAnswer& answer)
        {
            std::string text = answer.status != 0 ? std::to_string(answer.status) : std::string("no answer");
            if (!answer.error.empty())
                text += " (" + answer.error + ")";
            return text;
        }

        // The persisted identity: POSIX keeps the inode only (the device is compared within a run).
        FileKey KeyOf(const std::string& name, const SourceFacts& facts)
        {
            return FileKey{ name, facts.size, facts.modified_ticks, facts.identity };
        }
    } // namespace

    struct LargeFiles::Impl
    {
        // What one item (a watched file or a handed-over file) is while it is processed.
        struct Item
        {
            std::uint64_t owner = 0;
            std::shared_ptr<WatchState> watch;
            std::shared_ptr<FileState> file;
            std::string path;
            std::string name;
            const ResolvedOptions* options = nullptr;
            std::string sensor;
            std::string registration_json;
            std::string state_folder; // <data>/large-files
            FileKey key;
            bool has_key = false;
        };

        enum class Result
        {
            Stored,
            Final,
            GivenUp,
            Missing,
            Later,
            Changed,
            Busy,
            Skip,
            Cut,
        };

        struct Outcome
        {
            Result result = Result::Later;
            std::string reason;
            std::int64_t not_before_ms = 0;
        };

        struct Observation
        {
            std::int64_t size = -1;
            std::int64_t modified = 0;
            std::int64_t unchanged_since = 0;
            std::int64_t not_before = 0;
            int busy = 0;
        };

        struct WatchRun
        {
            std::shared_ptr<WatchState> watch;
            std::unique_ptr<FolderState> state;
            bool state_usable = false;
            bool state_busy = false;
            std::int64_t next_scan = 0;
            std::map<std::string, Observation> seen; // by name
            std::set<std::string> done_this_run;     // name|size|mtime: skipped until it changes
            std::string folder_problem;
        };

        LargeFiles& owner_;
        LargeFileShared& shared_;

        // Guarded by shared_.mutex.
        std::vector<std::shared_ptr<WatchState>> watches_;
        std::deque<std::shared_ptr<FileState>> files_;
        bool running_ = false;
        bool scan_now_ = false;
        std::string build_unavailable_;

        std::atomic<bool> stop_{ false };
        std::thread thread_;
        std::atomic<std::thread::id> thread_id_{};
        std::atomic<std::uintptr_t> thread_token_{ 0 };
        RunContext context_;

        // The uploader thread's own.
        std::map<std::uint64_t, WatchRun> runs_;
        std::unique_ptr<FileConnection> connection_;
        std::uint64_t connection_generation_ = 0;
        std::int64_t connection_retry_at_ = 0;
        int connection_attempt_ = 0;
        std::uint64_t absent_generation_ = 0;
        std::int64_t absent_until_ = 0;
        std::map<std::string, std::uint64_t> registered_; // sensor -> session generation
        std::map<std::string, std::string> refused_sensors_;
        std::map<std::uint64_t, std::int64_t> file_not_before_;
        std::set<std::string> said_;
        std::set<std::string> counted_;
        std::set<std::string> final_this_run_; // path|size|mtime refused for this run
        std::vector<std::uint8_t> source_;
        std::vector<std::uint8_t> frame_;
        std::unique_ptr<tables::ZstdCompressor> zstd_;
        std::int64_t next_send_at_ = 0;
        bool stale_checked_ = false;

        Impl(LargeFiles& owner, LargeFileShared& shared)
            : owner_(owner), shared_(shared)
        {
        }

        // The reason "cannot be sent" was said for last; said again only for another reason.
        std::string unavailable_said_;
        bool thread_make_failed_ = false;

        // Under shared_.mutex: why nothing can be sent now (the build, the transport the collector chose,
        // or the run's).
        std::string UnavailableReason() const
        {
            if (!build_unavailable_.empty())
                return build_unavailable_;
            if (!transport_unavailable_.empty())
                return transport_unavailable_;
            return running_ ? context_.unavailable_reason : std::string();
        }

        std::string transport_unavailable_;
        std::function<void(bool, const std::string&)> reporter_;

        // Under shared_.mutex: the line that says it, once per run.
        std::string TakeUnavailableLine()
        {
            const std::string reason = UnavailableReason();
            if (reason.empty() || reason == unavailable_said_)
                return std::string();
            unavailable_said_ = reason;
            return "Large files cannot be sent: " + reason + ".";
        }

        // ---- Small helpers ----

        std::int64_t Now() const { return context_.now_ms ? context_.now_ms() : 0; }

        void Report(bool error, const std::string& message) noexcept
        {
            try
            {
                if (context_.report)
                    context_.report(error, message);
                else if (reporter_)
                    reporter_(error, message);
            }
            catch (...)
            {
            }
        }

        // Once per run for `key`.
        void Say(const std::string& key, bool error, const std::string& message)
        {
            if (said_.insert(key).second)
                Report(error, message);
        }

        // One file not sent: said and counted once per file and run.
        void NotSent(const Item& item, const std::string& key, const std::string& message)
        {
            if (!counted_.insert(item.path + "|" + key).second)
                return;
            if (item.watch)
                item.watch->files_not_sent.fetch_add(1);
            try
            {
                if (context_.count_lost_file)
                    context_.count_lost_file();
            }
            catch (...)
            {
            }
            Report(true, message);
        }

        bool Stopping(const Item* item) const
        {
            if (stop_.load())
                return true;
            if (item == nullptr)
                return false;
            if (item->watch && item->watch->released.load())
                return true;
            if (item->file && item->file->cancelled.load())
                return true;
            return false;
        }

        // Waits `ms` on the collector's clock, servicing the file connection; false when stopping.
        // `idle`: the wait for nothing in particular also ends at a wake (a new watch or file, a scan, a
        // cancel), so they are acted on at once.
        bool Wait(std::int64_t ms, const std::string& what, const Item* item, bool idle = false)
        {
            // The deadline first: a test's hook may move the manual clock by the wait.
            const std::int64_t until = Now() + (std::max<std::int64_t>)(ms, 0);
            if (owner_.on_wait_for_tests)
            {
                try
                {
                    owner_.on_wait_for_tests(what, ms);
                }
                catch (...)
                {
                }
            }
            std::uint64_t first = 0;
            {
                std::lock_guard<std::mutex> guard(shared_.mutex);
                first = shared_.wakes;
            }
            for (;;)
            {
                if (Stopping(item))
                    return false;
                if (Now() >= until)
                    return true;
                ServiceConnection();
                std::unique_lock<std::mutex> lock(shared_.mutex);
                if (idle && shared_.wakes != first)
                    return true;
                const std::uint64_t seen = shared_.wakes;
                shared_.cv.wait_for(lock, std::chrono::milliseconds(kSliceMs), [&] { return stop_.load() || shared_.wakes != seen; });
            }
        }

        void ServiceConnection()
        {
            if (!connection_ || !connection_->IsOpen())
                return;
            if (std::chrono::steady_clock::now() >= connection_->NextPingDue())
                connection_->Service();
        }

        void CloseConnection()
        {
            if (connection_ && connection_->IsOpen())
                connection_->Close(0, 1000, std::string());
        }

        // After a cut: the connection is closed and may be opened again.
        void ResetConnection()
        {
            if (!connection_)
                return;
            connection_->Close(0, 1000, std::string());
            connection_->ResetCancel();
            connection_generation_ = 0;
        }

        std::int64_t Backoff()
        {
            const int attempt = (std::min)(connection_attempt_++, 6);
            return (std::min)(kMaxBackoffMs, static_cast<std::int64_t>(1000) << attempt);
        }

        // ---- The connection and the registration ----

        // True when a file connection of the live session is open.
        bool EnsureConnection(const Item& item, Outcome& outcome)
        {
            hsm::http::SessionIdentity session;
            try
            {
                session = context_.session ? context_.session() : hsm::http::SessionIdentity{};
            }
            catch (...)
            {
            }
            if (session.generation == 0)
            {
                CloseConnection();
                outcome = Outcome{ Result::Later, "the collector session is not up", Now() + 1000 };
                return false;
            }
            if (connection_ && connection_->IsOpen() && connection_generation_ == session.generation)
                return true;
            CloseConnection();
            if (session.session_id.empty())
            {
                Say("no-session-id", true, "Large files cannot be sent: the server's session has no id (a server before table and file connections).");
                outcome = Outcome{ Result::Later, "the server has no file connections", Now() + 1000 };
                return false;
            }
            if (absent_generation_ == session.generation && Now() < absent_until_)
            {
                // Looked at again every second: a new session lifts it at once.
                outcome = Outcome{ Result::Later, "the server has no large files", Now() + 1000 };
                return false;
            }
            if (Now() < connection_retry_at_)
            {
                outcome = Outcome{ Result::Later, "waiting to open the file connection again", connection_retry_at_ };
                return false;
            }
            if (!connection_)
                connection_ = std::make_unique<FileConnection>(context_.connection);
            connection_->ResetCancel();
            SetCut(item.owner);
            const auto opened = connection_->Open(session.session_id);
            SetCut(0);
            if (opened.ok)
            {
                connection_generation_ = session.generation;
                connection_attempt_ = 0;
                return true;
            }
            if (Stopping(&item))
            {
                outcome = Outcome{ Result::Cut, std::string(), 0 };
                return false;
            }
            if (opened.status == 404 || opened.status == 400)
            {
                absent_generation_ = session.generation;
                absent_until_ = Now() + kFeatureAbsentRetryMs;
                Say("absent", true, "Large files cannot be sent: the server has no file connections (the upgrade was answered " + std::to_string(opened.status) + "); files wait and the server is asked again with the next session and every 30 minutes.");
                outcome = Outcome{ Result::Later, "the server has no large files", Now() + 1000 };
                return false;
            }
            const std::int64_t wait = opened.status == 409 ? 1000 : Backoff();
            connection_retry_at_ = Now() + wait;
            Say("open:" + opened.kind, true, "The file connection could not be opened: " + opened.why);
            outcome = Outcome{ Result::Later, opened.why, connection_retry_at_ };
            return false;
        }

        // The File sensor's registration on the session, once per session; false with `outcome`.
        bool EnsureRegistered(const Item& item, Outcome& outcome)
        {
            const auto refused = refused_sensors_.find(item.sensor);
            if (refused != refused_sensors_.end())
            {
                outcome = Outcome{ Result::Final, refused->second, 0 };
                return false;
            }
            hsm::http::SessionIdentity session;
            try
            {
                session = context_.session ? context_.session() : hsm::http::SessionIdentity{};
            }
            catch (...)
            {
            }
            if (session.generation == 0)
            {
                outcome = Outcome{ Result::Later, "the collector session is not up", Now() + 1000 };
                return false;
            }
            const auto done = registered_.find(item.sensor);
            if (done != registered_.end() && done->second == session.generation)
                return true;
            hsm::http::SessionResult answer;
            try
            {
                answer = context_.commands("[" + item.registration_json + "]");
            }
            catch (...)
            {
                answer = hsm::http::SessionResult{};
                answer.error = "the registration request failed";
            }
            if (answer.acked && answer.status == 200)
            {
                const auto body = nlohmann::json::parse(answer.body.empty() ? std::string("{}") : answer.body, nullptr, false);
                if (body.is_object())
                {
                    const auto error = body.find(item.sensor);
                    if (error != body.end())
                    {
                        const std::string why = "the server refused the registration of the File sensor " + item.sensor + ": " +
                                                (error->is_string() ? error->get<std::string>() : error->dump());
                        refused_sensors_[item.sensor] = why;
                        outcome = Outcome{ Result::Final, why, 0 };
                        return false;
                    }
                }
                registered_[item.sensor] = session.generation;
                return true;
            }
            if (answer.acked && answer.status >= 400 && answer.status < 500)
            {
                const std::string why = "the server refused the registration of the File sensor " + item.sensor + " (" + std::to_string(answer.status) + " " +
                                        answer.error + ")";
                refused_sensors_[item.sensor] = why;
                outcome = Outcome{ Result::Final, why, 0 };
                return false;
            }
            outcome = Outcome{ Result::Later, "the registration got no answer: " + answer.error, Now() + Backoff() };
            return false;
        }

        void SetCut(std::uint64_t owner)
        {
            std::lock_guard<std::mutex> guard(shared_.mutex);
            shared_.current_owner = owner;
            if (owner == 0)
                shared_.cut = nullptr;
            else
            {
                FileConnection* connection = connection_.get();
                shared_.cut = [connection] {
                    if (connection != nullptr)
                        connection->Cancel();
                };
            }
        }

        // ---- The state of an item ----

        std::unique_ptr<FolderState> SentState(const Item& item, std::string& note, bool& usable)
        {
            std::string error;
            usable = false;
            if (!MakeFolders(item.state_folder, error))
            {
                Say("state-folder:" + item.state_folder, true, "The data folder of large files cannot be used (" + error + "); files are sent without their state.");
                return nullptr;
            }
            auto state = std::make_unique<FolderState>(JoinPath(item.state_folder, "sent-" + Sha16(item.sensor) + ".json"), std::string(), std::string(), item.sensor);
            const std::int64_t deadline = Now() + 5000;
            for (;;)
            {
                const auto locked = state->Load(note, error);
                if (locked == LockedStateFile::Result::Ok)
                {
                    usable = true;
                    return state;
                }
                if (locked == LockedStateFile::Result::Failed || Now() >= deadline || !Wait(100, "the lock of a state file", &item))
                    return nullptr;
            }
        }

        FolderState* WatchStateOf(const Item& item)
        {
            if (!item.watch)
                return nullptr;
            const auto run = runs_.find(item.watch->id);
            return run != runs_.end() && run->second.state_usable ? run->second.state.get() : nullptr;
        }

        // ---- One item ----

        Outcome Process(Item& item)
        {
            if (!context_.unavailable_reason.empty())
                return Outcome{ Result::Final, context_.unavailable_reason, 0 };
            // A handed-over file refused in this run is not tried again before the next Start.
            if (item.file && final_this_run_.count(item.path + "|" + item.name) != 0)
                return Outcome{ Result::Final, "it was refused earlier in this run", 0 };

            Outcome outcome;
            if (!EnsureRegistered(item, outcome))
                return outcome;

            SourceFile source;
            std::string error;
            switch (source.Open(item.path, error))
            {
            case OpenResult::Ok:
                break;
            case OpenResult::Busy:
                return Outcome{ Result::Busy, error, 0 };
            case OpenResult::Missing:
                return Outcome{ Result::Missing, "the file is not there (" + error + ")", 0 };
            case OpenResult::Link:
                return Outcome{ Result::Missing, "the file is a link or a reparse point and is never taken", 0 };
            case OpenResult::Denied:
            case OpenResult::Failed:
                return Outcome{ Result::Missing, "the file cannot be read (" + error + ")", 0 };
            }

            SourceFacts start;
            if (!source.Facts(start, error))
                return Outcome{ Result::Missing, "the file cannot be read (" + error + ")", 0 };
            if (item.watch)
            {
                const auto run = runs_.find(item.watch->id);
                if (run != runs_.end())
                {
                    const auto seen = run->second.seen.find(item.name);
                    if (seen == run->second.seen.end() || seen->second.size != start.size || seen->second.modified != start.modified_ticks)
                        return Outcome{ Result::Changed, "the file changed when it was taken", 0 };
                }
            }

            const FileKey key = KeyOf(item.name, start);
            item.key = key;
            item.has_key = true;
            std::unique_ptr<FolderState> sent_state;
            FolderState* state = WatchStateOf(item);
            if (item.file)
            {
                std::string note;
                bool usable = false;
                sent_state = SentState(item, note, usable);
                if (!note.empty())
                    Say("note:" + note, true, note);
                state = usable ? sent_state.get() : nullptr;
            }

            if (state != nullptr)
            {
                if (const auto* given_up = state->FindGivenUp(key))
                    return Outcome{ Result::GivenUp, given_up->reason, 0 };
                if (state->FindDelivered(key) != nullptr && item.options->keep_source)
                {
                    if (sent_state)
                        sent_state->Unload();
                    return Outcome{ Result::Skip, "delivered before", 0 };
                }
            }
            if (sent_state)
                sent_state->Unload(); // locked again only for a write

            if (!EnsureConnection(item, outcome))
                return outcome;
            const FileHello hello = connection_->Hello();
            if (start.size > hello.max_file_bytes)
                return Outcome{ Result::Final,
                                "the file is larger than the server takes (" + std::to_string(start.size) + " bytes, the limit is " + std::to_string(hello.max_file_bytes) + ")",
                                0 };

            // The record of the upload in progress.
            std::string note;
            const bool state_folder_ok = MakeFolders(item.state_folder, error);
            UploadRecord record(JoinPath(item.state_folder, "upload-" + Sha16(PathKeyForm(item.path)) + ".json"));
            bool exists = false;
            bool have_record = false;
            if (state_folder_ok)
            {
                const auto locked = record.Load(exists, note, error);
                if (!note.empty())
                    Say("note:" + note, true, note);
                if (locked == LockedStateFile::Result::Busy)
                {
                    Say("busy:" + item.path, true, "Another collector process uploads " + item.path + "; it is left to that process.");
                    return Outcome{ Result::Later, "another process uploads it", Now() + ScanIntervalMs(item.options->settle_ms) };
                }
                have_record = locked == LockedStateFile::Result::Ok;
            }
            bool own_record = state != nullptr && state->FindDelivered(key) != nullptr;
            if (exists && !(record.Facts().key == key && record.Facts().sensor == item.sensor))
            {
                // The record of another file under this path: that upload is discarded first.
                const Outcome discarded = DiscardRecorded(item, record);
                if (discarded.result == Result::Cut || discarded.result == Result::Later)
                    return discarded;
                exists = false;
            }
            if (exists)
                own_record = true;
            if (!exists)
            {
                record.Facts() = UploadFacts{};
                record.Facts().path = item.path;
                record.Facts().sensor = item.sensor;
                record.Facts().key = key;
            }

            Outcome result = Upload(item, source, start, hello, record, have_record);
            if (result.result == Result::Stored)
                Finish(item, source, start, key, record, have_record, own_record, result);
            else if (result.result == Result::GivenUp || result.result == Result::Final || result.result == Result::Changed)
            {
                if (have_record)
                    record.Remove();
            }
            else
                record.Unload();
            return result;
        }

        bool SaveRecord(UploadRecord& record, bool have_record)
        {
            if (!have_record)
                return true;
            std::string error;
            if (!record.Save(error))
            {
                Say("record-save", true, "The record of a large file upload could not be written (" + error + "); a restart reads its pieces again.");
                return false;
            }
            return true;
        }

        // What a lost connection means; `counted` true when the close is a protocol error counted for the file.
        Outcome Lost(const Item& item, UploadRecord& record, bool have_record, const FileAnswer& answer)
        {
            if (Stopping(&item))
                return Outcome{ Result::Cut, std::string(), 0 };
            if (answer.kind == FileAnswerKind::Cancelled)
            {
                // A cut meant for an upload that ended meanwhile: the connection is opened again.
                ResetConnection();
                return Outcome{ Result::Later, std::string(), Now() };
            }
            const auto& end = connection_->LastEnd();
            const int code = end.server_close != 0 ? end.server_close : (end.protocol_error ? end.close_code : 0);
            if (code == 1008)
                return Outcome{ Result::Final, "the server closed the file connection with 1008: the access key may no longer send", 0 };
            if (code == 1002 || code == 1007 || code == 1009)
            {
                ++record.Facts().protocol_closes;
                SaveRecord(record, have_record);
                if (record.Facts().protocol_closes >= kMaxProtocolCloses)
                    return Outcome{ Result::GivenUp, "the file connection was closed with " + std::to_string(code) + " " + std::to_string(kMaxProtocolCloses) + " times", 0 };
                Say("close:" + std::to_string(code), true, "The server closed the file connection with " + std::to_string(code) + ": " + end.why);
            }
            connection_retry_at_ = Now() + (code == 1000 ? 0 : Backoff());
            return Outcome{ Result::Later, "the file connection was lost: " + answer.error, connection_retry_at_ };
        }

        // begin, the pieces and end, until the file is stored or the attempt ends.
        Outcome Upload(Item& item, SourceFile& source, const SourceFacts& start, const FileHello& hello, UploadRecord& record, bool have_record)
        {
            FileFacts facts{ item.sensor, item.name, start.size, start.modified_ticks, start.identity };
            std::int64_t piece_bytes = record.Facts().piece_bytes > 0 && record.Facts().piece_bytes <= hello.piece_bytes ? record.Facts().piece_bytes : hello.piece_bytes;
            int in_a_row = 0;
            SetCut(item.owner);
            struct Clear
            {
                Impl* self;
                ~Clear() { self->SetCut(0); }
            } clear{ this };

            for (;;)
            {
                if (Stopping(&item))
                    return Outcome{ Result::Cut, std::string(), 0 };
                Outcome reopen;
                if (!EnsureConnection(item, reopen))
                    return reopen;
                SetCut(item.owner);

                FileAnswer begun = connection_->Begin(facts, piece_bytes);
                switch (begun.kind)
                {
                case FileAnswerKind::Next:
                    break;
                case FileAnswerKind::Stored:
                    return Outcome{ Result::Stored, std::string(), 0 };
                case FileAnswerKind::Checking:
                    if (!Wait(begun.retry_after_ms > 0 ? begun.retry_after_ms : kDefaultRetryMs, "begin: the server checks the file", &item))
                        return Outcome{ Result::Cut, std::string(), 0 };
                    continue;
                case FileAnswerKind::RetryLater:
                    return Outcome{ Result::Later, "begin: " + Status(begun), Now() + (begun.retry_after_ms > 0 ? begun.retry_after_ms : kDefaultRetryMs) };
                case FileAnswerKind::Final:
                    return Outcome{ Result::Final, "the server refused the file at begin: " + Status(begun), 0 };
                case FileAnswerKind::Lost:
                case FileAnswerKind::NotOpen:
                case FileAnswerKind::Cancelled:
                    return Lost(item, record, have_record, begun);
                default:
                    return Outcome{ Result::Later, "begin: " + Status(begun), Now() + Backoff() };
                }
                if (begun.piece_bytes > hello.piece_bytes)
                {
                    // An upload begun under a larger hello: begin it again with this connection's size (the
                    // server starts it from 0).
                    piece_bytes = hello.piece_bytes;
                    if (record.Facts().piece_bytes == piece_bytes)
                        return Outcome{ Result::Later, "the server keeps a piece size larger than its hello", Now() + Backoff() };
                    record.Facts().piece_bytes = piece_bytes;
                    continue;
                }
                piece_bytes = begun.piece_bytes;
                // The digests of the pieces depend on the content and the piece size only: they hold for
                // another upload of the same file, not for another piece size.
                const bool size_changed = record.Facts().piece_bytes != 0 && record.Facts().piece_bytes != piece_bytes;
                record.Facts().upload = begun.upload;
                record.Facts().piece_bytes = piece_bytes;
                SaveRecord(record, have_record);
                std::string error;
                if (!record.OpenDigests(begun.pieces, error) && have_record)
                    Say("digests", true, "The digest list of a large file upload cannot be kept (" + error + "); a restart reads its pieces again.");
                if (size_changed)
                {
                    const Sha256Digest zero{};
                    for (std::int64_t i = 0; i < begun.pieces; ++i)
                        record.SetDigest(i, zero, error);
                }
                Report(false, "Large file " + item.path + ": begin on " + item.sensor + " (" + std::to_string(start.size) + " bytes, " + std::to_string(begun.pieces) +
                                  " pieces, from piece " + std::to_string(begun.next) + ").");

                std::int64_t next = begun.next;
                const std::int64_t pieces = begun.pieces;
                bool restart = false;
                while (!restart)
                {
                    while (next < pieces)
                    {
                        const Outcome sent = SendPiece(item, source, start, record, have_record, piece_bytes, pieces, next, in_a_row, restart);
                        if (sent.result != Result::Later || sent.not_before_ms != -1)
                            return sent;
                        if (restart)
                            break;
                    }
                    if (restart)
                        break;

                    // Every piece is in: the digests the list lacks are read again, the file compared.
                    if (!FillDigests(item, source, record, piece_bytes, pieces, start.size))
                        return Stopping(&item) ? Outcome{ Result::Cut, std::string(), 0 } : Outcome{ Result::Missing, "the file cannot be read again", 0 };
                    if (Changed(item, source, start))
                        return DiscardChanged(item, record);
                    Sha256 whole;
                    for (std::int64_t i = 0; i < pieces; ++i)
                    {
                        Sha256Digest digest{};
                        record.Digest(i, digest);
                        whole.Update(digest.data(), digest.size());
                    }
                    Sha256Digest hash{};
                    if (!whole.Final(hash))
                        return Outcome{ Result::Later, "SHA-256 failed", Now() + Backoff() };

                    for (;;)
                    {
                        FileAnswer ended = connection_->End(record.Facts().upload, ToHex(hash.data(), hash.size()));
                        if (ended.kind == FileAnswerKind::Checking)
                        {
                            if (!Wait(ended.retry_after_ms > 0 ? ended.retry_after_ms : kDefaultRetryMs, "end: the server checks the file", &item))
                                return Outcome{ Result::Cut, std::string(), 0 };
                            continue;
                        }
                        switch (ended.kind)
                        {
                        case FileAnswerKind::Stored:
                            return Outcome{ Result::Stored, std::string(), ended.stored_ticks };
                        case FileAnswerKind::Missing:
                            next = ended.next;
                            break;
                        case FileAnswerKind::Discarded: {
                            const Outcome discarded = OnDiscarded(record, have_record, "end: " + Status(ended));
                            if (discarded.result != Result::Later || discarded.not_before_ms != -1)
                                return discarded;
                            restart = true;
                            break;
                        }
                        case FileAnswerKind::Final:
                            return Outcome{ Result::Final, "the server refused the file at end: " + Status(ended), 0 };
                        case FileAnswerKind::NotHeld:
                            restart = true;
                            break;
                        case FileAnswerKind::RetryLater:
                            if (!Wait(ended.retry_after_ms > 0 ? ended.retry_after_ms : kDefaultRetryMs, "end: the server could not read the file", &item))
                                return Outcome{ Result::Cut, std::string(), 0 };
                            restart = true;
                            break;
                        case FileAnswerKind::Lost:
                        case FileAnswerKind::NotOpen:
                        case FileAnswerKind::Cancelled:
                            return Lost(item, record, have_record, ended);
                        default:
                            return Outcome{ Result::Later, "end: " + Status(ended), Now() + Backoff() };
                        }
                        break;
                    }
                }
            }
        }

        // A 400 that discarded the upload: counted, given up at the third.
        Outcome OnDiscarded(UploadRecord& record, bool have_record, const std::string& what)
        {
            ++record.Facts().discards;
            record.Facts().upload.clear();
            SaveRecord(record, have_record);
            if (record.Facts().discards >= kMaxDiscards)
                return Outcome{ Result::GivenUp, "the server discarded the upload " + std::to_string(kMaxDiscards) + " times (the last: " + what + ")", 0 };
            Report(true, "The server discarded a large file upload (" + what + "); it is sent again from the first piece.");
            return Outcome{ Result::Later, std::string(), -1 };
        }

        // One piece: read, hash, compress, the speed limit, send. Later with not_before -1: go on (next was
        // moved); `restart`: begin again.
        Outcome SendPiece(Item& item, SourceFile& source, const SourceFacts& start, UploadRecord& record, bool have_record, std::int64_t piece_bytes,
                          std::int64_t pieces, std::int64_t& next, int& in_a_row, bool& restart)
        {
            const std::int64_t offset = next * piece_bytes;
            const std::size_t length = static_cast<std::size_t>((std::min)(piece_bytes, start.size - offset));
            std::string error;
            try
            {
                if (source_.size() < static_cast<std::size_t>(piece_bytes))
                    source_.resize(static_cast<std::size_t>(piece_bytes));
                frame_.clear();
                frame_.reserve(kPieceHeaderBytes + ZSTD_compressBound(static_cast<std::size_t>(piece_bytes)));
            }
            catch (const std::bad_alloc&)
            {
                Say("memory", true, "Memory for a large file piece could not be allocated; the upload is tried again later.");
                return Outcome{ Result::Later, "no memory for a piece", Now() + Backoff() };
            }
            if (!source.ReadAt(offset, source_.data(), length, [this, &item] { return Stopping(&item); }, error))
                return Stopping(&item) ? Outcome{ Result::Cut, std::string(), 0 } : Outcome{ Result::Changed, "the file changed while it was sent: " + error, 0 };
            Sha256Digest digest{};
            if (!Sha256Of(source_.data(), length, digest))
                return Outcome{ Result::Later, "SHA-256 failed", Now() + Backoff() };
            frame_.assign(kPieceHeaderBytes, 0);
            if (!zstd_)
                zstd_ = std::make_unique<tables::ZstdCompressor>(3);
            const char* zstd_error = nullptr;
            if (!zstd_->CompressFrame(source_.data(), length, frame_, zstd_error))
                return Outcome{ Result::Later, std::string("zstd failed: ") + (zstd_error != nullptr ? zstd_error : "?"), Now() + Backoff() };

            // The speed limit: bytes sent, waited for between frames, never inside one.
            const std::int64_t limit = item.options->max_bytes_per_second;
            if (limit > 0)
            {
                const std::int64_t now = Now();
                if (next_send_at_ > now && !Wait(next_send_at_ - now, "the speed limit", &item))
                    return Outcome{ Result::Cut, std::string(), 0 };
                const std::int64_t began = (std::max)(Now(), next_send_at_);
                next_send_at_ = began + static_cast<std::int64_t>((static_cast<double>(frame_.size()) * 1000.0) / static_cast<double>(limit));
            }

            const auto written_at = std::chrono::steady_clock::now();
            FileAnswer answer = connection_->Piece(record.Facts().upload, next, static_cast<std::uint32_t>(length), digest, frame_);
            const auto took = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - written_at).count();
            const std::int64_t heartbeat = connection_->Hello().heartbeat_ms > 0 ? connection_->Hello().heartbeat_ms : 30000;
            if (took >= 3 * heartbeat && took > 0)
                Say("slow-link", true, "The link to the server passes about " + std::to_string(static_cast<std::int64_t>(frame_.size()) / (std::max<std::int64_t>)(took, 1)) + " KB/s; a piece of " + std::to_string(frame_.size()) + " bytes must arrive within the server's frame deadline.");

            switch (answer.kind)
            {
            case FileAnswerKind::Next:
                if (answer.status == 200 && answer.next > next)
                    record.SetDigest(next, digest, error);
                in_a_row = 0;
                next = (std::min)(answer.next, pieces);
                return Outcome{ Result::Later, std::string(), -1 };
            case FileAnswerKind::ChecksumMismatch:
                ++record.Facts().checksum_mismatches;
                SaveRecord(record, have_record);
                if (record.Facts().checksum_mismatches >= kMaxChecksumMismatches)
                    return Outcome{ Result::GivenUp, "the server found " + std::to_string(kMaxChecksumMismatches) + " pieces with a wrong checksum", 0 };
                if (++in_a_row >= kChecksumMismatchesInARow)
                {
                    in_a_row = 0;
                    CloseConnection();
                    restart = true;
                }
                return Outcome{ Result::Later, std::string(), -1 };
            case FileAnswerKind::RetryLater:
                if (!Wait(answer.retry_after_ms > 0 ? answer.retry_after_ms : kDefaultRetryMs, "a piece: the server could not write it", &item))
                    return Outcome{ Result::Cut, std::string(), 0 };
                return Outcome{ Result::Later, std::string(), -1 };
            case FileAnswerKind::NotHeld:
                restart = true;
                return Outcome{ Result::Later, std::string(), -1 };
            case FileAnswerKind::Discarded: {
                const Outcome discarded = OnDiscarded(record, have_record, "a piece: " + Status(answer));
                restart = discarded.result == Result::Later && discarded.not_before_ms == -1;
                return discarded;
            }
            case FileAnswerKind::Lost:
            case FileAnswerKind::NotOpen:
            case FileAnswerKind::Cancelled:
                return Lost(item, record, have_record, answer);
            default:
                return Outcome{ Result::Later, "a piece: " + Status(answer), Now() + Backoff() };
            }
        }

        // Reads again the pieces whose digest the list lacks (a restart without a list, a crash between an
        // ack and its write). Not counted by the speed limit.
        bool FillDigests(Item& item, SourceFile& source, UploadRecord& record, std::int64_t piece_bytes, std::int64_t pieces, std::int64_t size)
        {
            std::string error;
            if (source_.size() < static_cast<std::size_t>(piece_bytes))
                source_.resize(static_cast<std::size_t>(piece_bytes));
            for (std::int64_t i = 0; i < pieces; ++i)
            {
                Sha256Digest digest{};
                if (record.Digest(i, digest))
                    continue;
                ServiceConnection();
                const std::int64_t offset = i * piece_bytes;
                const std::size_t length = static_cast<std::size_t>((std::min)(piece_bytes, size - offset));
                if (!source.ReadAt(offset, source_.data(), length, [this, &item] { return Stopping(&item); }, error))
                    return false;
                if (!Sha256Of(source_.data(), length, digest))
                    return false;
                record.SetDigest(i, digest, error);
            }
            return true;
        }

        // The file compared with what it was begun as: size, time, identity on the open handle; on POSIX
        // also the path still names the same device and inode.
        bool Changed(const Item& item, SourceFile& source, const SourceFacts& start)
        {
            SourceFacts now;
            std::string error;
            if (!source.Facts(now, error) || !now.SameAs(start))
                return true;
#if !defined(_WIN32)
            SourceFacts at_path;
            if (!PathFacts(item.path, at_path, error) || at_path.identity != start.identity || at_path.device != start.device)
                return true;
#else
            (void)item;
#endif
            return false;
        }

        // A file that changed in the middle: its upload is discarded on the server and it is taken again
        // when it has settled.
        Outcome DiscardChanged(Item& item, UploadRecord& record)
        {
            Report(true, "Large file " + item.path + " changed while it was sent; the upload is discarded and the file is sent again once it has settled.");
            const Outcome discarded = DiscardRecorded(item, record);
            if (discarded.result == Result::Cut || discarded.result == Result::Later)
                return discarded;
            return Outcome{ Result::Changed, "the file changed while it was sent", 0 };
        }

        // Discards the upload a record names: on this connection, or after a begin with the facts it was begun with.
        Outcome DiscardRecorded(Item& item, UploadRecord& record)
        {
            const FileFacts original{ record.Facts().sensor, record.Facts().key.name, record.Facts().key.size, record.Facts().key.modified_ticks,
                                      record.Facts().key.identity };
            for (int round = 0; round < 8; ++round)
            {
                if (Stopping(&item))
                    return Outcome{ Result::Cut, std::string(), 0 };
                Outcome reopen;
                if (!EnsureConnection(item, reopen))
                    return reopen;
                SetCut(item.owner);
                std::string upload = record.Facts().upload;
                if (upload.empty())
                {
                    const FileAnswer begun = connection_->Begin(original, record.Facts().piece_bytes > 0 ? record.Facts().piece_bytes : connection_->Hello().piece_bytes);
                    if (begun.kind == FileAnswerKind::Stored || begun.kind == FileAnswerKind::Final)
                    {
                        record.Remove();
                        return Outcome{ Result::Changed, std::string(), 0 };
                    }
                    if (begun.kind == FileAnswerKind::Checking || begun.kind == FileAnswerKind::RetryLater)
                    {
                        if (!Wait(begun.retry_after_ms > 0 ? begun.retry_after_ms : kDefaultRetryMs, "discard: begin with the original facts", &item))
                            return Outcome{ Result::Cut, std::string(), 0 };
                        continue;
                    }
                    if (begun.kind != FileAnswerKind::Next)
                        return Outcome{ Result::Later, "discard: " + Status(begun), Now() + Backoff() };
                    upload = begun.upload;
                    record.Facts().upload = upload;
                }
                const FileAnswer answer = connection_->Discard(upload);
                switch (answer.kind)
                {
                case FileAnswerKind::DiscardDone:
                    record.Remove();
                    return Outcome{ Result::Changed, std::string(), 0 };
                case FileAnswerKind::UnknownOp:
                    Say("discard-unknown", true, "The server has no discard: the upload of a changed file stays there until it expires.");
                    record.Remove();
                    return Outcome{ Result::Changed, std::string(), 0 };
                case FileAnswerKind::NotHeld:
                    record.Facts().upload.clear();
                    continue;
                case FileAnswerKind::Checking:
                case FileAnswerKind::RetryLater:
                    if (!Wait(answer.retry_after_ms > 0 ? answer.retry_after_ms : kDefaultRetryMs, "discard", &item))
                        return Outcome{ Result::Cut, std::string(), 0 };
                    record.Facts().upload.clear();
                    continue;
                default:
                    return Outcome{ Result::Later, "discard: " + Status(answer), Now() + Backoff() };
                }
            }
            return Outcome{ Result::Later, "discard did not settle", Now() + Backoff() };
        }

        // Stored: the delivered entry first, then the delete of exactly the file that was sent.
        void Finish(Item& item, SourceFile& source, const SourceFacts& start, const FileKey& key, UploadRecord& record, bool have_record, bool own_record, Outcome& result)
        {
            const std::int64_t stored_ticks = result.not_before_ms;
            result.not_before_ms = 0;
            std::string error;
            std::string note;
            if (item.watch)
            {
                if (auto* state = WatchStateOf(item))
                {
                    state->SetDelivered(key, stored_ticks);
                    if (!state->Save(error))
                        Say("state-save:" + item.state_folder, true, "The state of large files could not be written (" + error + ").");
                }
            }
            else
            {
                bool usable = false;
                auto sent = SentState(item, note, usable);
                if (usable)
                {
                    sent->SetDelivered(key, stored_ticks);
                    if (!sent->Save(error))
                        Say("state-save:" + item.state_folder, true, "The state of large files could not be written (" + error + ").");
                    sent->Unload();
                }
            }
            if (have_record)
                record.Remove();
            else
                record.Unload();

            bool deleted = false;
            if (!item.options->keep_source)
            {
                if (start.identity.empty() && !own_record)
                    Report(true, "Large file " + item.path + " was stored by the server before, but this collector has no record of sending it and the file has no identity; it is kept.");
                else if (Changed(item, source, start))
                    Report(true, "Large file " + item.path + " was stored, but it changed after it was sent; it is not deleted and is a new file.");
                else
                {
                    switch (source.DeleteThis(item.path, error))
                    {
                    case DeleteResult::Deleted:
                        deleted = true;
                        break;
                    case DeleteResult::Changed:
                        Report(true, "Large file " + item.path + " was stored, but the file at its path is no longer the one sent; it is not deleted.");
                        break;
                    case DeleteResult::Failed:
                        Report(true, "Large file " + item.path + " was stored, but it could not be deleted: " + error);
                        break;
                    }
                }
            }
            source.Close();
            if (item.watch)
                item.watch->files_stored.fetch_add(1);
            if (item.file)
            {
                item.file->source_deleted.store(deleted);
                item.file->stored_ticks.store(stored_ticks);
                item.file->Settle(HSM_LARGE_FILE_STORED, std::string());
            }
            Report(false, "Large file " + item.path + " is stored on " + item.sensor + (deleted ? "; the source was deleted." : "."));
        }

        // ---- The loop ----

        void SyncWatches()
        {
            std::vector<std::shared_ptr<WatchState>> live;
            {
                std::lock_guard<std::mutex> guard(shared_.mutex);
                live = watches_;
            }
            for (auto it = runs_.begin(); it != runs_.end();)
            {
                const bool present = std::any_of(live.begin(), live.end(), [&](const std::shared_ptr<WatchState>& w) { return w->id == it->first; });
                if (!present || it->second.watch->released.load())
                {
                    if (it->second.state)
                        it->second.state->Unload();
                    it = runs_.erase(it);
                }
                else
                    ++it;
            }
            for (const auto& watch : live)
            {
                if (watch->released.load() || runs_.count(watch->id) != 0)
                    continue;
                WatchRun run;
                run.watch = watch;
                if (!context_.unavailable_reason.empty())
                {
                    watch->state.store(HSM_LARGE_FILE_WATCH_UNAVAILABLE);
                    watch->SetReason(context_.unavailable_reason);
                }
                runs_.emplace(watch->id, std::move(run));
            }
        }

        void LoadWatchState(WatchRun& run)
        {
            if (run.state_usable)
                return;
            auto& watch = *run.watch;
            const std::string folder = JoinPath(watch.data_folder, "large-files");
            std::string error;
            if (!MakeFolders(folder, error))
            {
                Say("state-folder:" + folder, true, "The data folder of large files cannot be used (" + error + "); files are sent without their state.");
                return;
            }
            if (!run.state)
                run.state = std::make_unique<FolderState>(JoinPath(folder, "watch-" + Sha16(PathKeyForm(watch.folder) + "|" + watch.mask + "|" + watch.sensor) + ".json"),
                                                          watch.folder, watch.mask, watch.sensor);
            std::string note;
            const auto locked = run.state->Load(note, error);
            if (!note.empty())
                Say("note:" + note, true, note);
            if (locked == LockedStateFile::Result::Ok)
            {
                run.state_usable = true;
                run.state_busy = false;
                return;
            }
            if (locked == LockedStateFile::Result::Busy)
            {
                run.state_busy = true;
                Say("watched-elsewhere:" + std::to_string(watch.id), true,
                    "Another collector process watches " + watch.folder + " with the mask " + watch.mask + " for " + watch.sensor + "; this watch uploads nothing until it is free.");
                watch.SetReason("another collector process watches the same folder, mask and sensor");
                return;
            }
            Say("state-failed:" + folder, true, "The state of large files cannot be used (" + error + "); files are sent without it.");
        }

        void Scan(WatchRun& run, bool force)
        {
            const std::int64_t now = Now();
            auto& watch = *run.watch;
            if (!force && now < run.next_scan)
                return;
            run.next_scan = now + ScanIntervalMs(watch.options.settle_ms);
            LoadWatchState(run);
            std::vector<FolderEntry> entries;
            std::string error;
            const auto listed = ListFolder(watch.folder, entries, error);
            if (listed != ListResult::Ok)
            {
                if (run.folder_problem != error)
                {
                    run.folder_problem = error;
                    Report(true, "The watched folder " + watch.folder + " cannot be read (" + error + "); it is scanned again.");
                    watch.SetReason(error);
                }
                return;
            }
            if (!run.folder_problem.empty())
            {
                run.folder_problem.clear();
                watch.SetReason(std::string());
            }
            std::set<std::string> present;
            std::map<std::string, Observation> seen;
            for (const auto& entry : entries)
            {
                if (!entry.name_valid)
                {
                    if (MaskMatches(watch.mask, entry.raw_name))
                    {
                        Item item;
                        item.watch = run.watch;
                        item.path = JoinPath(watch.folder, entry.raw_name);
                        NotSent(item, "bad-name", "A file in the watched folder " + watch.folder + " has a name that is not UTF-8; it is not sent.");
                    }
                    continue;
                }
                if (!MaskMatches(watch.mask, entry.name))
                    continue;
                present.insert(entry.name);
                if (entry.is_link)
                {
                    Item item;
                    item.watch = run.watch;
                    item.path = JoinPath(watch.folder, entry.name);
                    NotSent(item, "link", "Large file " + item.path + " is a link or a reparse point and is never taken.");
                    continue;
                }
                Observation observation;
                const auto before = run.seen.find(entry.name);
                if (before != run.seen.end() && before->second.size == entry.size && before->second.modified == entry.modified_ticks)
                    observation = before->second;
                else
                {
                    observation.size = entry.size;
                    observation.modified = entry.modified_ticks;
                    observation.unchanged_since = now;
                }
                seen.emplace(entry.name, observation);
            }
            run.seen = std::move(seen);
            if (run.state_usable && run.state->Prune(present))
            {
                if (!run.state->Save(error))
                    Say("state-save:" + watch.data_folder, true, "The state of large files could not be written (" + error + ").");
            }
        }

        // The next item: handed-over files first, then the oldest settled file of every watch.
        bool Pick(Item& item, std::int64_t& wake_at)
        {
            const std::int64_t now = Now();
            wake_at = now + 60000;
            {
                std::lock_guard<std::mutex> guard(shared_.mutex);
                for (auto it = files_.begin(); it != files_.end();)
                {
                    if ((*it)->cancelled.load() || (*it)->outcome.load() != HSM_LARGE_FILE_PENDING)
                    {
                        it = files_.erase(it);
                        continue;
                    }
                    ++it;
                }
                for (const auto& file : files_)
                {
                    const auto not_before = file_not_before_.find(file->id);
                    if (not_before != file_not_before_.end() && not_before->second > now)
                    {
                        wake_at = (std::min)(wake_at, not_before->second);
                        continue;
                    }
                    item = Item{};
                    item.owner = file->id;
                    item.file = file;
                    item.path = file->path;
                    item.name = FileNameOf(file->path);
                    item.options = &file->options;
                    item.sensor = file->sensor;
                    item.registration_json = file->registration_json;
                    item.state_folder = JoinPath(file->data_folder, "large-files");
                    return true;
                }
            }

            const WatchRun* best_run = nullptr;
            std::string best_name;
            std::int64_t best_modified = 0;
            for (auto& entry : runs_)
            {
                auto& run = entry.second;
                const auto& watch = *run.watch;
                wake_at = (std::min)(wake_at, run.next_scan);
                if (watch.state.load() != HSM_LARGE_FILE_WATCH_ACTIVE || run.state_busy)
                    continue;
                for (const auto& seen : run.seen)
                {
                    const auto& observation = seen.second;
                    const std::string done_key = seen.first + "|" + std::to_string(observation.size) + "|" + std::to_string(observation.modified);
                    if (run.done_this_run.count(done_key) != 0 || final_this_run_.count(JoinPath(watch.folder, seen.first) + "|" + done_key) != 0)
                        continue;
                    const std::int64_t settled_at = observation.unchanged_since + watch.options.settle_ms;
                    if (settled_at > now)
                    {
                        wake_at = (std::min)(wake_at, settled_at);
                        continue;
                    }
                    if (observation.not_before > now)
                    {
                        wake_at = (std::min)(wake_at, observation.not_before);
                        continue;
                    }
                    // Taken once per watch only: a file that two watches match is the first watch's.
                    bool earlier = false;
                    for (const auto& other : runs_)
                    {
                        if (other.first >= entry.first)
                            break;
                        if (PathKeyForm(other.second.watch->folder) == PathKeyForm(watch.folder) && other.second.seen.count(seen.first) != 0)
                            earlier = true;
                    }
                    if (earlier)
                        continue;
                    if (best_run == nullptr || observation.modified < best_modified || (observation.modified == best_modified && seen.first < best_name))
                    {
                        best_run = &run;
                        best_name = seen.first;
                        best_modified = observation.modified;
                    }
                }
            }
            if (best_run == nullptr)
                return false;
            item = Item{};
            item.owner = best_run->watch->id;
            item.watch = best_run->watch;
            item.path = JoinPath(best_run->watch->folder, best_name);
            item.name = best_name;
            item.options = &best_run->watch->options;
            item.sensor = best_run->watch->sensor;
            item.registration_json = best_run->watch->registration_json;
            item.state_folder = JoinPath(best_run->watch->data_folder, "large-files");
            return true;
        }

        void Apply(Item& item, const Outcome& outcome)
        {
            const std::int64_t now = Now();
            Observation* observation = nullptr;
            WatchRun* run = nullptr;
            if (item.watch)
            {
                const auto found = runs_.find(item.watch->id);
                if (found != runs_.end())
                {
                    run = &found->second;
                    const auto seen = run->seen.find(item.name);
                    if (seen != run->seen.end())
                        observation = &seen->second;
                }
            }
            const std::string done_key =
                observation != nullptr ? item.name + "|" + std::to_string(observation->size) + "|" + std::to_string(observation->modified) : item.name;

            switch (outcome.result)
            {
            case Result::Stored:
                if (run != nullptr)
                    run->seen.erase(item.name);
                if (run != nullptr && item.options->keep_source)
                    run->done_this_run.insert(done_key);
                break;
            case Result::Skip:
                if (run != nullptr)
                    run->done_this_run.insert(done_key);
                if (item.file)
                    item.file->Settle(HSM_LARGE_FILE_STORED, std::string());
                break;
            case Result::Final:
                if (item.watch && outcome.reason.find("refused the registration") != std::string::npos)
                {
                    item.watch->state.store(HSM_LARGE_FILE_WATCH_REFUSED);
                    item.watch->SetReason(outcome.reason);
                }
                if (item.watch && outcome.reason == context_.unavailable_reason)
                {
                    item.watch->state.store(HSM_LARGE_FILE_WATCH_UNAVAILABLE);
                    item.watch->SetReason(outcome.reason);
                }
                final_this_run_.insert(item.path + "|" + done_key);
                if (item.file)
                    item.file->Settle(outcome.reason == context_.unavailable_reason ? HSM_LARGE_FILE_UNAVAILABLE : HSM_LARGE_FILE_REFUSED, outcome.reason);
                if (outcome.reason != context_.unavailable_reason)
                    NotSent(item, "final", "Large file " + item.path + " is not sent: " + outcome.reason + ". It is left where it is and tried again after the next start.");
                else
                    Say("unavailable", true, "Large files cannot be sent: " + outcome.reason + ".");
                break;
            case Result::GivenUp: {
                std::string error;
                if (item.has_key && item.watch)
                {
                    if (auto* state = WatchStateOf(item))
                    {
                        state->SetGivenUp(item.key, outcome.reason);
                        if (!state->Save(error))
                            Say("state-save:" + item.state_folder, true, "The state of large files could not be written (" + error + ").");
                    }
                }
                else if (item.has_key)
                {
                    std::string note;
                    bool usable = false;
                    auto sent = SentState(item, note, usable);
                    if (usable)
                    {
                        sent->SetGivenUp(item.key, outcome.reason);
                        if (!sent->Save(error))
                            Say("state-save:" + item.state_folder, true, "The state of large files could not be written (" + error + ").");
                        sent->Unload();
                    }
                }
                final_this_run_.insert(item.path + "|" + done_key);
                if (item.file)
                    item.file->Settle(HSM_LARGE_FILE_GIVEN_UP, outcome.reason);
                NotSent(item, "given-up", "Large file " + item.path + " is given up: " + outcome.reason + ". It is left where it is and offered again only when it changes.");
                break;
            }
            case Result::Missing:
                if (run != nullptr)
                    run->seen.erase(item.name);
                if (item.file)
                    item.file->Settle(HSM_LARGE_FILE_MISSING, outcome.reason);
                NotSent(item, "missing", "Large file " + item.path + " is not sent: " + outcome.reason + ".");
                break;
            case Result::Changed:
                if (observation != nullptr)
                {
                    observation->size = -1; // observed again from the next scan
                    observation->unchanged_since = now;
                }
                if (run != nullptr)
                    run->seen.erase(item.name);
                break;
            case Result::Busy:
                if (observation != nullptr)
                {
                    observation->unchanged_since = now;
                    if (++observation->busy == kHeldOpenReportAfter)
                        Report(true, "Large file " + item.path + " is held open by another process (" + outcome.reason + "); it is taken when nobody holds it.");
                }
                if (item.file)
                {
                    std::lock_guard<std::mutex> guard(shared_.mutex);
                    file_not_before_[item.file->id] = now + 1000;
                }
                break;
            case Result::Later:
                if (observation != nullptr)
                    observation->not_before = outcome.not_before_ms;
                if (item.file)
                {
                    std::lock_guard<std::mutex> guard(shared_.mutex);
                    file_not_before_[item.file->id] = outcome.not_before_ms;
                }
                if (!outcome.reason.empty())
                    Say("later:" + outcome.reason, false, "Large files wait: " + outcome.reason + ".");
                break;
            case Result::Cut:
                break;
            }
        }

        // Upload records whose file is gone or changed (a crash, a restart): their uploads are discarded.
        void CheckStaleRecords()
        {
            if (stale_checked_)
                return;
            stale_checked_ = true;
            std::set<std::string> folders;
            {
                std::lock_guard<std::mutex> guard(shared_.mutex);
                for (const auto& watch : watches_)
                    folders.insert(JoinPath(watch->data_folder, "large-files"));
                for (const auto& file : files_)
                    folders.insert(JoinPath(file->data_folder, "large-files"));
            }
            for (const auto& folder : folders)
                for (const auto& name : ListUploadRecords(folder))
                {
                    if (stop_.load())
                        return;
                    UploadRecord record(name);
                    bool exists = false;
                    std::string note, error;
                    if (record.Load(exists, note, error) != LockedStateFile::Result::Ok || !exists)
                    {
                        record.Unload();
                        continue;
                    }
                    SourceFacts facts;
                    const bool same = PathFacts(record.Facts().path, facts, error) && KeyOf(record.Facts().key.name, facts) == record.Facts().key;
                    if (same)
                    {
                        record.Unload(); // continued when its file is taken
                        continue;
                    }
                    Item item;
                    item.path = record.Facts().path;
                    item.sensor = record.Facts().sensor;
                    const Outcome discarded = DiscardRecorded(item, record);
                    if (discarded.result != Result::Changed)
                    {
                        record.Unload();
                        stale_checked_ = false; // asked again on a later loop
                        return;
                    }
                }
        }

        void Run() noexcept
        {
            try
            {
                std::string error;
                LowerCurrentThreadPriority(error);
                if (!error.empty())
                    Report(false, "Large files: " + error + " (the upload goes on at normal priority).");
            }
            catch (...)
            {
            }
            while (!stop_.load())
            {
                try
                {
                    Loop();
                }
                catch (...)
                {
                    Say("loop-fault", true, "The large file uploader met an unexpected failure; it goes on.");
                    Wait(1000, "after a failure", nullptr);
                }
            }
            CloseConnection();
            for (auto& run : runs_)
                if (run.second.state)
                    run.second.state->Unload();
            runs_.clear();
        }

        void Loop()
        {
            SyncWatches();
            bool force = false;
            {
                std::lock_guard<std::mutex> guard(shared_.mutex);
                force = scan_now_;
                scan_now_ = false;
            }
            for (auto& run : runs_)
                if (run.second.watch->state.load() == HSM_LARGE_FILE_WATCH_ACTIVE)
                    Scan(run.second, force);
            if (context_.unavailable_reason.empty())
                CheckStaleRecords();

            Item item;
            std::int64_t wake_at = 0;
            if (!Pick(item, wake_at))
            {
                CloseConnection();
                const std::int64_t now = Now();
                Wait((std::max<std::int64_t>)(1, (std::min<std::int64_t>)(wake_at - now, 1000)), "nothing to upload", nullptr, /*idle=*/true);
                return;
            }
            Outcome outcome;
            try
            {
                outcome = Process(item);
            }
            catch (const std::bad_alloc&)
            {
                outcome = Outcome{ Result::Later, "out of memory", Now() + Backoff() };
            }
            if (outcome.result == Result::Cut)
            {
                ResetConnection();
                if (item.file && item.file->cancelled.load())
                    item.file->Settle(HSM_LARGE_FILE_CANCELLED, "cancelled");
            }
            Apply(item, outcome);
        }
    };
#else
    struct LargeFiles::Impl
    {
        std::vector<std::shared_ptr<WatchState>> watches_;
        std::deque<std::shared_ptr<FileState>> files_;
        std::string build_unavailable_;
        std::atomic<bool> stop_{ false };
        bool running_ = false;

        std::string UnavailableReason() const { return build_unavailable_; }
        std::string TakeUnavailableLine() { return std::string(); }
        void Report(bool, const std::string&) noexcept {}
    };
#endif

    LargeFiles::LargeFiles()
        : shared_(std::make_shared<LargeFileShared>())
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        impl_ = std::make_unique<Impl>(*this, *shared_);
#else
        impl_ = std::make_unique<Impl>();
#endif
    }

    LargeFiles::~LargeFiles()
    {
        Stop();
    }

    bool LargeFiles::AddWatch(const std::shared_ptr<WatchState>& watch, std::string& error)
    {
        bool make_thread = false;
        std::string say;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            for (const auto& other : impl_->watches_)
                if (!other->released.load() && other->sensor == watch->sensor && other->mask == watch->mask && other->folder == watch->folder)
                {
                    error = "a watch of the same folder, mask and sensor path exists";
                    return false;
                }
            impl_->watches_.erase(std::remove_if(impl_->watches_.begin(), impl_->watches_.end(), [](const std::shared_ptr<WatchState>& w) { return w->released.load(); }),
                                  impl_->watches_.end());
            const std::string reason = impl_->UnavailableReason();
            if (!reason.empty())
            {
                watch->state.store(HSM_LARGE_FILE_WATCH_UNAVAILABLE);
                watch->SetReason(reason);
                say = impl_->TakeUnavailableLine();
            }
            impl_->watches_.push_back(watch);
            make_thread = reason.empty() && impl_->running_;
        }
        if (!say.empty())
            impl_->Report(true, say);
        if (make_thread)
            MakeThread();
        shared_->Wake();
        return true;
    }

    void LargeFiles::AddFile(const std::shared_ptr<FileState>& file)
    {
        bool make_thread = false;
        std::string say;
        std::string reason;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            reason = impl_->UnavailableReason();
            if (reason.empty())
            {
                impl_->files_.push_back(file);
                make_thread = impl_->running_;
            }
            else
                say = impl_->TakeUnavailableLine();
        }
        if (!reason.empty())
            file->Settle(HSM_LARGE_FILE_UNAVAILABLE, reason);
        if (!say.empty())
            impl_->Report(true, say);
        if (make_thread)
            MakeThread();
        shared_->Wake();
    }

    std::shared_ptr<FileState> LargeFiles::FindPendingFile(const std::string& path, const std::string& sensor)
    {
        std::lock_guard<std::mutex> guard(shared_->mutex);
        for (const auto& file : impl_->files_)
            if (file->path == path && file->sensor == sensor && !file->cancelled.load() && file->outcome.load() == HSM_LARGE_FILE_PENDING)
                return file;
        return nullptr;
    }

    void LargeFiles::SetBuildUnavailable(const std::string& reason)
    {
        std::lock_guard<std::mutex> guard(shared_->mutex);
        impl_->build_unavailable_ = reason;
    }

    void LargeFiles::SetReporter(std::function<void(bool error, const std::string& message)> report)
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        std::lock_guard<std::mutex> guard(shared_->mutex);
        impl_->reporter_ = std::move(report);
#else
        (void)report;
#endif
    }

    void LargeFiles::SetTransportUnavailable(const std::string& reason)
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        std::vector<std::shared_ptr<FileState>> unavailable;
        std::string say;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            impl_->transport_unavailable_ = reason;
            const std::string now = impl_->UnavailableReason();
            for (const auto& watch : impl_->watches_)
            {
                if (!now.empty())
                {
                    watch->state.store(HSM_LARGE_FILE_WATCH_UNAVAILABLE);
                    watch->SetReason(now);
                }
                else if (watch->state.load() == HSM_LARGE_FILE_WATCH_UNAVAILABLE)
                {
                    watch->state.store(HSM_LARGE_FILE_WATCH_ACTIVE);
                    watch->SetReason(std::string());
                }
            }
            if (!now.empty())
            {
                unavailable.assign(impl_->files_.begin(), impl_->files_.end());
                impl_->files_.clear();
                if (!unavailable.empty() || !impl_->watches_.empty())
                    say = impl_->TakeUnavailableLine();
            }
        }
        for (const auto& file : unavailable)
            file->Settle(HSM_LARGE_FILE_UNAVAILABLE, reason);
        if (!say.empty())
            impl_->Report(true, say);
#else
        (void)reason;
#endif
    }

#if defined(HSM_COLLECTOR_LARGE_FILES)
    void LargeFiles::Start(RunContext context)
    {
        std::vector<std::shared_ptr<FileState>> unavailable;
        bool make_thread = false;
        std::string line;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            if (impl_->thread_.joinable())
                return;
            impl_->context_ = std::move(context);
            impl_->running_ = true;
            impl_->stop_.store(false);
            // A new run: final refusals and the registrations of the last run are forgotten.
            impl_->registered_.clear();
            impl_->refused_sensors_.clear();
            impl_->final_this_run_.clear();
            impl_->said_.clear();
            impl_->counted_.clear();
            impl_->file_not_before_.clear();
            impl_->stale_checked_ = false;
            impl_->connection_generation_ = 0;
            impl_->absent_until_ = 0;
            const std::string& reason = impl_->context_.unavailable_reason;
            for (const auto& watch : impl_->watches_)
            {
                watch->state.store(reason.empty() ? HSM_LARGE_FILE_WATCH_ACTIVE : HSM_LARGE_FILE_WATCH_UNAVAILABLE);
                watch->SetReason(reason);
            }
            if (!reason.empty())
            {
                unavailable.assign(impl_->files_.begin(), impl_->files_.end());
                impl_->files_.clear();
                if (!impl_->watches_.empty() || !unavailable.empty())
                    line = impl_->TakeUnavailableLine();
            }
            else
                make_thread = !impl_->watches_.empty() || !impl_->files_.empty();
        }
        for (const auto& file : unavailable)
            file->Settle(HSM_LARGE_FILE_UNAVAILABLE, impl_->context_.unavailable_reason);
        if (!line.empty())
            impl_->Report(true, line);
        if (make_thread)
            MakeThread();
    }

    void LargeFiles::MakeThread()
    {
        std::lock_guard<std::mutex> guard(shared_->mutex);
        if (impl_->thread_.joinable() || !impl_->running_)
            return;
        try
        {
            impl_->thread_ = std::thread([this] {
                impl_->thread_id_.store(std::this_thread::get_id());
                impl_->thread_token_.store(CurrentThreadToken());
                impl_->Run();
            });
        }
        catch (...)
        {
            impl_->thread_make_failed_ = true;
        }
    }
#endif

    void LargeFiles::Stop() noexcept
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        std::thread thread;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            impl_->running_ = false;
            impl_->stop_.store(true);
            thread = std::move(impl_->thread_);
            ++shared_->wakes;
        }
        if (thread.joinable())
        {
            std::function<void()> cut;
            {
                std::lock_guard<std::mutex> guard(shared_->mutex);
                cut = shared_->cut;
            }
            if (cut)
                cut();
            shared_->cv.notify_all();
            const std::uintptr_t token = impl_->thread_token_.load();
            RaiseThreadPriority(token);
            CancelThreadIo(token);
            thread.join();
            {
                std::lock_guard<std::mutex> guard(shared_->mutex);
                shared_->current_owner = 0;
                shared_->cut = nullptr;
            }
            impl_->thread_token_.store(0);
            impl_->thread_id_.store(std::thread::id{});
            ReleaseThreadToken(token);
        }
#endif
    }

    void LargeFiles::Dispose(const std::function<void(bool, const std::string&)>& report, const std::function<void()>& count_lost) noexcept
    {
        Stop();
        std::vector<std::shared_ptr<FileState>> pending;
        std::vector<std::shared_ptr<WatchState>> watches;
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
            shared_->disposed = true;
            pending.assign(impl_->files_.begin(), impl_->files_.end());
            impl_->files_.clear();
            watches.swap(impl_->watches_);
        }
        std::size_t lost = 0;
        for (const auto& file : pending)
            if (file->Settle(HSM_LARGE_FILE_CANCELLED, "the collector was disposed before the file was sent"))
                ++lost;
        for (const auto& watch : watches)
            watch->state.store(HSM_LARGE_FILE_WATCH_STOPPED);
        if (lost > 0)
        {
            try
            {
                for (std::size_t i = 0; i < lost; ++i)
                    if (count_lost)
                        count_lost();
                if (report)
                    report(true, std::to_string(lost) + " handed-over large file(s) were not sent: the collector was disposed first.");
            }
            catch (...)
            {
            }
        }
    }

    bool LargeFiles::IsUploaderThread() const noexcept
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        return impl_->thread_id_.load() == std::this_thread::get_id();
#else
        return false;
#endif
    }

    void LargeFiles::ScanNowForTests()
    {
        {
            std::lock_guard<std::mutex> guard(shared_->mutex);
#if defined(HSM_COLLECTOR_LARGE_FILES)
            impl_->scan_now_ = true;
#endif
        }
        shared_->Wake();
    }

    bool LargeFiles::UploaderAliveForTests() const
    {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        std::lock_guard<std::mutex> guard(shared_->mutex);
        return impl_->thread_.joinable();
#else
        return false;
#endif
    }
} // namespace hsm::collector::large_files
