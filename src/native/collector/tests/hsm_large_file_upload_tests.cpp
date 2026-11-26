// Large file uploads of the native collector end to end (#1887): the C ABI, a real collector on the session
// of the scripted test server, its file side (tests/file_server_script.hpp), files on the disk. The
// collector runs on a manual clock; every wait of the uploader advances it by what it waits for (and is
// recorded), except its idle wait, so settle times are driven by the test alone. A wait in a test only
// fails it when it expires.

#include "hsm_collector/hsm_collector.h"
#include "hsm_collector/hsm_collector.hpp"

#include <functional>
#include <map>
#include <string>

#if defined(HSM_COLLECTOR_LARGE_FILES)
#include "../src/large_files/hsm_large_file_mask.hpp"
#include "../src/large_files/hsm_large_file_platform.hpp"
#include "../src/large_files/hsm_large_file_state.hpp"
#include "../src/large_files/hsm_large_file_test_seams.hpp"
#include "../src/large_files/hsm_file_connection.hpp"
#include "../src/large_files/hsm_large_files.hpp"
#include "file_server_script.hpp"
#include "ws_test_server.hpp"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <random>
#include <stdexcept>
#include <thread>
#include <vector>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

extern "C" void hsm_collector_test_install_manual_clock(hsm_collector_t* collector, int64_t base_ms);
extern "C" void hsm_collector_test_advance_clock_ms(hsm_collector_t* collector, int64_t delta_ms);
#endif

namespace
{
#if defined(HSM_COLLECTOR_LARGE_FILES)
    namespace lf = hsm::collector::large_files;
    namespace fs = std::filesystem;
    using hsm::test::FileScriptAnswer;
    using hsm::test::FileServerScript;
    using Server = hsm::test::WebSocketTestServer;

    void Check(bool condition, const std::string& message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

    // Seeded content both drivers reproduce: xorshift32, one byte per step (the low byte of the state).
    std::string Content(std::size_t size, std::uint32_t seed)
    {
        std::uint32_t x = seed == 0 ? 1 : seed;
        std::string bytes(size, '\0');
        for (auto& byte : bytes)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            byte = static_cast<char>(x & 0xFF);
        }
        return bytes;
    }

    struct TempFolder
    {
        fs::path path;
        explicit TempFolder(const std::string& tag)
        {
            std::random_device device;
            path = fs::temp_directory_path() / ("hsm-lf-" + tag + "-" + std::to_string(device()));
            fs::create_directories(path);
        }
        ~TempFolder()
        {
            std::error_code code;
            fs::remove_all(path, code);
        }
        std::string Str() const { return path.u8string(); }
        std::string File(const std::string& name) const { return (path / name).u8string(); }
    };

    void WriteFile(const std::string& path, const std::string& bytes)
    {
        std::ofstream out(fs::u8path(path), std::ios::binary | std::ios::trunc);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    }

    void AppendFile(const std::string& path, const std::string& bytes)
    {
        std::ofstream out(fs::u8path(path), std::ios::binary | std::ios::app);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    }

    bool Exists(const std::string& path)
    {
        return fs::exists(fs::u8path(path));
    }

    void SetModified(const std::string& path, int seconds_ago)
    {
        std::error_code code;
        fs::last_write_time(fs::u8path(path), fs::file_time_type::clock::now() - std::chrono::seconds(seconds_ago), code);
    }

    template <class Predicate>
    bool WaitFor(Predicate predicate, int seconds = 20)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
        while (std::chrono::steady_clock::now() < deadline)
        {
            if (predicate())
                return true;
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        return predicate();
    }

    struct Waits
    {
        // The idle wait advances the clock too, unless a test drives the settle time itself.
        std::atomic<bool> idle_advances{ true };
        // A wait of this name (set before Start) does not move the clock: the uploader stays in it, and
        // `held` says it got there.
        std::string hold;
        std::atomic<bool> held{ false };
        std::mutex mutex;
        std::vector<std::pair<std::string, std::int64_t>> list;
        std::vector<std::pair<std::string, std::int64_t>> Copy()
        {
            std::lock_guard<std::mutex> guard(mutex);
            return list;
        }
    };

    struct LogLines
    {
        std::mutex mutex;
        std::vector<std::string> lines;
        std::string All()
        {
            std::lock_guard<std::mutex> guard(mutex);
            std::string text;
            for (const auto& line : lines)
                text += "\n  log: " + line;
            return text;
        }

        int Count(const std::string& text)
        {
            std::lock_guard<std::mutex> guard(mutex);
            int count = 0;
            for (const auto& line : lines)
                count += line.find(text) != std::string::npos ? 1 : 0;
            return count;
        }
    };

    constexpr std::int64_t kFrame = 70000; // pieces of 64 KiB

    // A collector on the session of the scripted server, its file side on.
    struct Rig
    {
        LogLines log;
        Waits waits;
        std::shared_ptr<FileServerScript> script;
        std::unique_ptr<Server> server;
        hsm_collector_t* collector = nullptr;
        TempFolder data{ "data" };
        TempFolder folder{ "watched" };

        static Server::Options ServerOptions()
        {
            Server::Options options;
            options.session = true;
            options.side_subprotocol = "hsm.files.v1";
            return options;
        }

        explicit Rig(std::shared_ptr<FileServerScript> shared_script = nullptr)
            : script(shared_script ? std::move(shared_script) : std::make_shared<FileServerScript>())
        {
            server = std::make_unique<Server>(ServerOptions());
            if (!shared_script)
                script->SetHello(kFrame, 30, 1LL << 40);
            server->EnableFileScript(script);
            Make();
        }

        void Make(bool session = true)
        {
            hsm_collector_options_t options{};
            options.access_key = "test-key";
            options.server_address = "http://127.0.0.1";
            options.port = server->Port();
            options.module = "module";
            options.computer_name = "host";
            options.allow_plaintext_transport = true;
            options.exception_deduplicator_window_ms = 0;
            Check(hsm_collector_create(&options, &collector) == HSM_RESULT_OK, "collector create");
            hsm_collector_set_logger(
                collector,
                [](hsm_log_level_t, const char* message, void* user) {
                    auto* sink = static_cast<LogLines*>(user);
                    std::lock_guard<std::mutex> guard(sink->mutex);
                    sink->lines.emplace_back(message != nullptr ? message : "");
                },
                &log);
            if (session)
                Check(hsm_collector_use_session_transport(collector) == HSM_RESULT_OK, "the session transport");
            hsm_collector_test_install_manual_clock(collector, 1000000);
            Check(hsm_collector_set_data_folder(collector, data.Str().c_str()) == HSM_RESULT_OK, "the data folder");
            hsm_collector_t* self = collector;
            Waits* recorded = &waits;
            lf::test::OnWait(collector, [self, recorded](const std::string& what, std::int64_t ms) {
                {
                    std::lock_guard<std::mutex> guard(recorded->mutex);
                    recorded->list.emplace_back(what, ms);
                }
                if (!recorded->hold.empty() && what == recorded->hold)
                {
                    recorded->held.store(true);
                    return;
                }
                if (what != "nothing to upload" || recorded->idle_advances.load())
                    hsm_collector_test_advance_clock_ms(self, ms);
            });
            // A request the script leaves unanswered ends when the script has decided so, never by a timeout.
            FileServerScript* scripted = script.get();
            lf::test::AdjustConnection(collector, [scripted](lf::FileConnectionOptions& connection) {
                connection.ack_abandoned_for_tests = [scripted](std::int64_t id) { return scripted->Unanswered(id); };
            });
        }

        ~Rig() { Destroy(); }

        void Destroy()
        {
            if (collector != nullptr)
                hsm_collector_destroy(collector);
            collector = nullptr;
        }

        // The same data folder, a new collector (a restart of the application).
        void Restart()
        {
            Destroy();
            Make();
        }

        void Start() { Check(hsm_collector_start(collector) == HSM_RESULT_OK, "start"); }

        // For a test that drives the settle time: the clock moves only when it says so, and the session
        // is up before it does (a first attempt before the session would wait on a clock that stands).
        void StartDriven()
        {
            waits.idle_advances.store(false);
            Start();
            Check(WaitFor([&] { return server->Upgrades() >= 1; }), "the session is up");
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
        void Advance(std::int64_t ms) { hsm_collector_test_advance_clock_ms(collector, ms); }
        void Scan() { lf::test::ScanNow(collector); }

        // Advances the clock in steps and scans, so a watched file settles.
        void Settle(std::int64_t ms)
        {
            Scan();
            std::this_thread::sleep_for(std::chrono::milliseconds(30));
            Advance(ms);
            Scan();
        }

        int Registrations(const std::string& needle)
        {
            int count = 0;
            for (const auto& text : server->Received())
                count += text.find("\"op\":\"commands\"") != std::string::npos && text.find(needle) != std::string::npos ? 1 : 0;
            return count;
        }
    };

    hsm_large_file_options_t NoLimit()
    {
        auto options = hsm_large_file_options_default();
        options.max_bytes_per_second = HSM_LARGE_FILE_NO_SPEED_LIMIT;
        return options;
    }

    hsm_large_file_result_t Result(hsm_large_file_t* file)
    {
        hsm_large_file_result_t result{};
        result.struct_size = sizeof result;
        hsm_large_file_get_result(file, &result);
        return result;
    }

    std::string Reason(hsm_large_file_t* file)
    {
        char buffer[512] = {};
        hsm_large_file_reason(file, buffer, sizeof buffer);
        return buffer;
    }

    bool Settled(hsm_large_file_t* file)
    {
        return Result(file).outcome != HSM_LARGE_FILE_PENDING;
    }

    // ---- Tests ----

    void NativeLargeFileMaskMatchesBothCollectorsRule()
    {
        Check(lf::MaskMatches("*.dmp", "a.dmp", false) && !lf::MaskMatches("*.dmp", "a.dmp_old", false), "*.dmp is the whole name (no 8.3 quirk)");
        Check(lf::MaskMatches("*.DMP", "a.dmp", true) && !lf::MaskMatches("*.DMP", "a.dmp", false), "ASCII case folded on Windows only");
        Check(lf::MaskMatches("a?c", "abc", false) && !lf::MaskMatches("a?c", "ac", false), "? is one character");
        Check(lf::MaskMatches("*", ".hidden", false) && lf::MaskMatches("*", "", false), "* matches a leading dot and nothing");
        Check(lf::MaskMatches("d*m*p", "dump", false) && !lf::MaskMatches("d*m*p", "dumpx", false), "stars backtrack");
        Check(lf::MaskMatches("?.dmp", "\xC3\xA9.dmp", false), "? is one UTF-8 character");
        std::string why;
        Check(!lf::CheckMask("[ab].dmp", why) && !lf::CheckMask("a;b", why) && !lf::CheckMask("a\\b", why) && !lf::CheckMask("a/b", why) && !lf::CheckMask("", why),
              "masks outside the shared subset are refused");
        Check(lf::CheckMask("*.dmp", why), "*.dmp is a mask");
        Check(!lf::MaskMatches("a?", "a", false) && lf::MaskMatches("a?", "ab", false) && !lf::MaskMatches("a?", "abc", false), "? is exactly one character");
        Check(!lf::MaskMatches("\xC3\x89.dmp", "\xC3\xA9.dmp", true), "case folding is ASCII only (E-acute is not folded)");
        Check(lf::MaskMatches("A?C.DMP", "abc.dmp", true) && !lf::MaskMatches("A?C.DMP", "abc.dmp", false), "folding applies around ? on Windows only");
        Check(!lf::CheckMask("a]", why) && !lf::CheckMask("a\tb", why) && !lf::CheckMask(std::string("a\x01b"), why), "] and control characters are refused");
    }

    void NativeLargeFileHandedFileIsStoredAndDeleted()
    {
        Rig rig;
        const std::string source = rig.folder.File("app.dmp");
        const std::string bytes = Content(200000, 7); // 4 pieces of 64 KiB
        WriteFile(source, bytes);
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }, 5), "the file must settle" + rig.log.All() + "\nalive=" + std::to_string(lf::test::UploaderAlive(rig.collector)) + " received=" + std::to_string(rig.server->Received().size()) + " begins=" + std::to_string(rig.script->Begins().size()) + " conns=" + std::to_string(rig.script->Connections().size()) + " waits=" + std::to_string(rig.waits.Copy().size()) +
                                                             (rig.waits.Copy().empty() ? std::string() : " first=" + rig.waits.Copy().front().first + " last=" + rig.waits.Copy().back().first));
        const auto result = Result(file);
        Check(result.outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
        Check(result.source_deleted && !Exists(source), "the source is deleted after stored");
        Check(result.stored_time_unix_ns != 0, "the value's time is reported");
        const auto stored = rig.script->StoredFiles();
        Check(stored.size() == 1 && stored.begin()->second == bytes, "the server reassembled the very bytes");
        const auto begins = rig.script->Begins();
        Check(begins.size() == 1 && begins[0].path == "host/module/dumps" && begins[0].name == "app.dmp" && begins[0].size == 200000 && begins[0].has_source,
              "begin names the sensor, the file and its identity");
        Check(begins[0].piece_bytes == 65536, "the piece size is the hello's");
        Check(rig.Registrations("\"DefaultAlertsOptions\":4") == 1, "the File sensor is registered with the alert flag 4");
        hsm_large_file_release(file);
    }

    void NativeLargeFileKeepModeIsNotSentAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("kept.dmp");
        WriteFile(source, Content(70000, 3));
        rig.Start();
        auto options = NoLimit();
        options.keep_source = 1;
        options.no_new_file_alert = 1;
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored");
        Check(Exists(source) && !Result(file).source_deleted, "a kept source stays");
        Check(rig.Registrations("\"DefaultAlertsOptions\":0") == 1, "without the alert the flag is not sent");
        hsm_large_file_release(file);

        rig.Restart();
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send again");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "a delivered kept file is stored at once");
        Check(rig.script->Begins().size() == 1, "and not begun again");
        hsm_large_file_release(file);
    }

    void NativeLargeFileSettleRuleOnAGrowingFile()
    {
        Rig rig;
        const std::string source = rig.folder.File("growing.dmp");
        WriteFile(source, Content(1000, 1));
        auto options = NoLimit();
        hsm_large_file_watch_t* watch = nullptr;
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &options, &watch) == HSM_RESULT_OK, "watch");
        rig.StartDriven();
        rig.Settle(59000);
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
        Check(rig.script->Begins().empty(), "not taken before the settle time");
        AppendFile(source, Content(1000, 2));
        rig.Settle(2000); // 61 s since the first scan, 2 s since it grew
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
        Check(rig.script->Begins().empty(), "a file that grew waits a whole settle time again");
        rig.Settle(60000);
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }), "taken once unchanged for the settle time");
        Check(WaitFor([&] { return !Exists(source); }), "deleted after stored");
        hsm_large_file_watch_stats_t stats{};
        stats.struct_size = sizeof stats;
        Check(WaitFor([&] { return hsm_large_file_watch_get_stats(watch, &stats) == HSM_RESULT_OK && stats.files_stored == 1; }) && stats.files_not_sent == 0,
              "the watch counts it");
        hsm_large_file_watch_release(watch);
    }

    void NativeLargeFileFilesFoundAtStartGoOldestFirst()
    {
        Rig rig;
        WriteFile(rig.folder.File("b.dmp"), Content(1000, 1));
        WriteFile(rig.folder.File("a.dmp"), Content(1000, 2));
        WriteFile(rig.folder.File("c.txt"), Content(1000, 3));
        SetModified(rig.folder.File("b.dmp"), 3600);
        SetModified(rig.folder.File("a.dmp"), 60);
        auto options = NoLimit();
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &options, nullptr) == HSM_RESULT_OK, "watch");
        rig.StartDriven();
        rig.Settle(60000);
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 2; }), "both files are stored");
        const auto begins = rig.script->Begins();
        Check(begins.size() == 2 && begins[0].name == "b.dmp" && begins[1].name == "a.dmp", "the oldest first");
        Check(Exists(rig.folder.File("c.txt")), "a file the mask does not match stays");
    }

    void NativeLargeFileSpeedLimitWaitsBetweenFrames()
    {
        Rig rig;
        const std::string source = rig.folder.File("slow.dmp");
        WriteFile(source, Content(4 * 65536, 9));
        rig.Start();
        auto options = hsm_large_file_options_default();
        options.max_bytes_per_second = 100000;
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored");
        std::vector<std::int64_t> limit_waits;
        for (const auto& wait : rig.waits.Copy())
            if (wait.first == "the speed limit")
                limit_waits.push_back(wait.second);
        const auto pieces = rig.script->Pieces();
        Check(pieces.size() == 4, "four pieces");
        Check(limit_waits.size() == 3, "a wait before every piece but the first (the burst is one frame): " + std::to_string(limit_waits.size()));
        for (std::size_t i = 0; i < limit_waits.size(); ++i)
        {
            const std::int64_t expected = static_cast<std::int64_t>(pieces[i].frame_bytes) * 1000 / 100000;
            Check(limit_waits[i] >= expected - 2 && limit_waits[i] <= expected + 2,
                  "the wait is the previous frame's wire bytes at the limit: " + std::to_string(limit_waits[i]) + " vs " + std::to_string(expected));
        }
        hsm_large_file_release(file);
    }

    void NativeLargeFileContinuesAfterADrop()
    {
        Rig rig;
        const std::string source = rig.folder.File("cut.dmp");
        const std::string bytes = Content(5 * 65536, 4);
        WriteFile(source, bytes);
        rig.script->AnswerPieceNext(FileScriptAnswer::Default());
        rig.script->AnswerPieceNext(FileScriptAnswer::Default());
        rig.script->AnswerPieceNext(FileScriptAnswer::Cut());
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored after the drop: " + Reason(file));
        const auto begins = rig.script->Begins();
        Check(begins.size() == 2, "begun again after the drop");
        std::vector<std::int64_t> numbers;
        for (const auto& piece : rig.script->Pieces())
            numbers.push_back(piece.number);
        Check(numbers == std::vector<std::int64_t>{ 0, 1, 2, 2, 3, 4 }, "continued from the server's next");
        Check(rig.script->StoredFiles().begin()->second == bytes, "the bytes");
        hsm_large_file_release(file);
    }

    void NativeLargeFileContinuesAfterARestart()
    {
        Rig rig;
        const std::string source = rig.folder.File("restart.dmp");
        const std::string bytes = Content(5 * 65536, 5);
        WriteFile(source, bytes);
        for (int i = 0; i < 3; ++i)
            rig.script->AnswerPieceNext(FileScriptAnswer::Default());
        rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer());
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.script->Pieces().size() == 4; }), "three pieces stored, the fourth unanswered");
        // A kill of the application in the middle: the collector is destroyed and made again.
        const auto stop_began = std::chrono::steady_clock::now();
        Check(hsm_collector_stop(rig.collector) == HSM_RESULT_OK, "stop");
        Check(std::chrono::steady_clock::now() - stop_began < std::chrono::seconds(4), "stop does not wait for the upload");
        Check(Result(file).outcome == HSM_LARGE_FILE_PENDING, "a stopped upload stays pending");
        hsm_large_file_release(file);
        rig.Restart();
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "handed over again");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored after the restart: " + Reason(file));
        std::vector<std::int64_t> numbers;
        for (const auto& piece : rig.script->Pieces())
            numbers.push_back(piece.number);
        Check(numbers == std::vector<std::int64_t>{ 0, 1, 2, 3, 3, 4 }, "continued from the server's next");
        Check(rig.script->StoredFiles().begin()->second == bytes, "the hash over the kept digests matched");
        hsm_large_file_release(file);
    }

    void CheckRefusal(int status, hsm_large_file_outcome_t expected)
    {
        Rig rig;
        const std::string source = rig.folder.File("refused.dmp");
        WriteFile(source, Content(1000, 1));
        rig.script->AnswerBeginNext(FileScriptAnswer::WithStatus(status, -1, std::string(), "scripted"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }), "settled");
        Check(Result(file).outcome == expected, "status " + std::to_string(status) + " gives its outcome: " + Reason(file));
        Check(Exists(source), "a refused file is left where it is");
        Check(rig.log.Count("refused.dmp is not sent") == 1, "said once");
        hsm_large_file_release(file);
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "again in the same run");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == expected, "not tried again before the next start");
        Check(rig.script->Begins().size() == 1, "no second begin");
        hsm_large_file_release(file);
    }

    void NativeLargeFileFinalRefusalsAreFinalForTheRun()
    {
        CheckRefusal(413, HSM_LARGE_FILE_REFUSED);
        CheckRefusal(422, HSM_LARGE_FILE_REFUSED);
        CheckRefusal(400, HSM_LARGE_FILE_REFUSED);
    }

    void NativeLargeFileNoRoomIsTriedAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("room.dmp");
        WriteFile(source, Content(1000, 1));
        rig.script->AnswerBeginNext(FileScriptAnswer::WithStatus(503, 60, std::string(), "no room"));
        rig.script->AnswerBeginNext(FileScriptAnswer::WithStatus(409, 1, std::string(), "busy"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        // The file waits on the collector's clock (60 s, then 1 s): advance it.
        Check(WaitFor([&] {
                  rig.Advance(1000);
                  return Settled(file);
              }),
              "settled");
        Check(Result(file).outcome == HSM_LARGE_FILE_STORED, "stored after no room and busy: " + Reason(file));
        Check(rig.script->Begins().size() == 3, "begun three times");
        hsm_large_file_release(file);
    }

    void NativeLargeFileThreeDiscardsGiveUp()
    {
        Rig rig;
        const std::string source = rig.folder.File("bad.dmp");
        WriteFile(source, Content(1000, 1));
        for (int i = 0; i < 3; ++i)
            rig.script->AnswerEndNext(FileScriptAnswer::WithStatus(400, -1, std::string(), "hash mismatch: the upload is discarded"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_GIVEN_UP, "given up: " + Reason(file));
        Check(Exists(source), "left where it is");
        hsm_large_file_release(file);
        rig.Restart();
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "again after a restart");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_GIVEN_UP, "the state remembers it");
        Check(rig.script->Ends().size() == 3, "not sent again while unchanged");
        hsm_large_file_release(file);
    }

    void NativeLargeFileClose1008IsFinal()
    {
        Rig rig;
        const std::string source = rig.folder.File("key.dmp");
        WriteFile(source, Content(70000, 1));
        rig.script->AnswerPieceNext(FileScriptAnswer::CloseWith(1008, "the access key can no longer send"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_REFUSED, "refused: " + Reason(file));
        hsm_large_file_release(file);
    }

    // The C++ wrapper: the options map to the C ABI's (zero = default, a zero limit = none), and the handles
    // read the outcome and the watch state.
    void NativeLargeFileCppWrapper()
    {
        hsm::collector::LargeFileOptions options;
        auto native = hsm::collector::detail::ToNative(options);
        Check(native.struct_size == sizeof native && native.settle_ms == 60000 && native.max_bytes_per_second == 10000000 && native.no_new_file_alert == 0 &&
                  native.keep_source == 0,
              "the wrapper's defaults are the ABI's");
        options.max_bytes_per_second = 0;
        options.new_file_alert = false;
        options.keep_source = true;
        native = hsm::collector::detail::ToNative(options);
        Check(native.max_bytes_per_second == HSM_LARGE_FILE_NO_SPEED_LIMIT && native.no_new_file_alert == 1 && native.keep_source == 1, "the wrapper maps its fields");

        Rig rig;
        const std::string source = rig.folder.File("wrapped.dmp");
        WriteFile(source, Content(1000, 1));
        rig.Start();
        auto c_options = NoLimit();
        hsm_large_file_t* handle = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &c_options, &handle) == HSM_RESULT_OK, "send");
        hsm::collector::LargeFile file(handle);
        Check(WaitFor([&] { return file.Outcome() != hsm::collector::LargeFileOutcome::Pending; }), "settled");
        Check(file.Outcome() == hsm::collector::LargeFileOutcome::Stored && file.SourceDeleted() && file.StoredTime().has_value() && file.Reason().empty(),
              "the wrapper reads the outcome: " + file.Reason());
        hsm_large_file_watch_t* watch_handle = nullptr;
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &c_options, &watch_handle) == HSM_RESULT_OK, "watch");
        {
            hsm::collector::LargeFileWatch watch(watch_handle);
            Check(watch.State() == hsm::collector::LargeFileWatchState::Active && watch.FilesStored() == 0, "the wrapper reads the watch");
        }
        // The watch was released with its wrapper: an equal one is allowed again.
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &c_options, nullptr) == HSM_RESULT_OK, "watch again");
    }

    // A state file that cannot be read is set aside and said; the kept file is offered again and the server
    // answers that it has it.
    void NativeLargeFileCorruptStateIsSetAside()
    {
        Rig rig;
        const std::string source = rig.folder.File("state.dmp");
        WriteFile(source, Content(1000, 1));
        rig.Start();
        auto options = NoLimit();
        options.keep_source = 1;
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored");
        hsm_large_file_release(file);
        rig.Destroy();

        std::string state;
        for (const auto& entry : fs::recursive_directory_iterator(rig.data.path))
            if (entry.path().filename().u8string().rfind("sent-", 0) == 0 && entry.path().extension() == ".json")
                state = entry.path().u8string();
        Check(!state.empty(), "the state of handed-over files is written");
        WriteFile(state, "{ not json");

        rig.Make();
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send again");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored again: " + Reason(file));
        Check(rig.script->Begins().size() == 2 && rig.script->StoredFiles().size() == 1, "begun again; the server answered stored");
        Check(rig.log.Count("could not be read") == 1, "the corrupt state is said");
        bool aside = false;
        for (const auto& entry : fs::recursive_directory_iterator(rig.data.path))
            aside = aside || entry.path().filename().u8string().find(".corrupt-") != std::string::npos;
        Check(aside, "the corrupt file is kept aside");
        Check(Exists(source), "a kept file stays");
        hsm_large_file_release(file);
    }

    // A piece with a wrong checksum is sent again; three in a row reconnect and begin again.
    void NativeLargeFileChecksumMismatchSendsThePieceAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("crc.dmp");
        const std::string bytes = Content(3 * 65536, 2);
        WriteFile(source, bytes);
        rig.script->CorruptNextPieceCrcs(4);
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
        std::vector<std::int64_t> numbers;
        for (const auto& piece : rig.script->Pieces())
            numbers.push_back(piece.number);
        Check(numbers == std::vector<std::int64_t>{ 0, 0, 0, 0, 0, 1, 2 }, "the same piece again until it passes");
        Check(rig.script->Begins().size() == 2, "three in a row: a new connection and a begin");
        Check(rig.script->StoredFiles().begin()->second == bytes, "the bytes");
        hsm_large_file_release(file);
    }

    // A piece without an answer is sent again on a new connection once the request timeout passed.
    void NativeLargeFileUnansweredPieceIsSentAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("silent.dmp");
        const std::string bytes = Content(3 * 65536, 3);
        WriteFile(source, bytes);
        rig.script->AnswerPieceNext(FileScriptAnswer::Default());
        rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer());
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor(
                  [&] {
                      rig.Advance(1000);
                      return Settled(file);
                  },
                  30),
              "settled" + rig.log.All());
        Check(Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
        std::vector<std::int64_t> numbers;
        for (const auto& piece : rig.script->Pieces())
            numbers.push_back(piece.number);
        Check(numbers == std::vector<std::int64_t>{ 0, 1, 1, 2 }, "the unanswered piece again");
        Check(rig.script->StoredFiles().begin()->second == bytes, "the bytes");
        hsm_large_file_release(file);
    }

    // ADR-0022 point 20: an ordinary file send above a quarter of the session's frame limit travels on the
    // file connection as a large value that is not secure; below it, a `list` frame as before.
    void NativeLargeFileOrdinarySendAboveTheThresholdIsRouted()
    {
        Rig rig;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "report", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        const std::string source = rig.folder.File("routed.bin");
        const std::string big = Content(1536 * 1024, 31); // above 4 MiB / 4
        WriteFile(source, big);
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        Check(hsm_sensor_add_file_from_path(sensor, source.c_str(), HSM_SENSOR_STATUS_WARNING, "big one") == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }), "routed to the file connection" + rig.log.All());
        const auto begins = rig.script->Begins();
        Check(begins.size() == 1 && begins[0].path == "host/module/reports" && begins[0].name == "routed.bin" && begins[0].size == static_cast<std::int64_t>(big.size()),
              "begin names the sensor and the file");
        Check(begins[0].raw.find("\"secure\":false") != std::string::npos, "a routed send is never secure: " + begins[0].raw);
        Check(begins[0].source.rfind("send:", 0) == 0 && begins[0].source.size() == 37, "source is send:<guid>: " + begins[0].source);
        Check(begins[0].raw.find("\"comment\":\"big one\"") != std::string::npos && begins[0].raw.find("\"status\":") != std::string::npos,
              "status and comment travel in begin: " + begins[0].raw);
        Check(rig.script->StoredFiles().begin()->second == big, "the bytes");
        Check(Exists(source), "nothing is deleted for a routed send");
        Check(rig.Registrations("\"DefaultAlertsOptions\":4") == 0, "no large-file registration for a routed send");

        // At or below the threshold: a `list` frame, as before.
        const std::string small = rig.folder.File("small.bin");
        WriteFile(small, Content(1000, 32));
        Check(hsm_sensor_add_file_from_path(sensor, small.c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "small send");
        Check(WaitFor([&] {
                  for (const auto& text : rig.server->Received())
                      if (text.find("\"op\":\"list\"") != std::string::npos && text.find("\"Type\":6") != std::string::npos)
                          return true;
                  return false;
              }),
              "a small file goes in a list frame");
        Check(rig.script->Begins().size() == 1, "and not on the file connection");
        hsm_sensor_release(sensor);
    }

    // Without file connections on the server: today's path (a list frame when it fits).
    void NativeLargeFileRouteMissingTakesTodaysPath()
    {
        Rig rig;
        rig.server->RefuseSideUpgradesWith(404);
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "report", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        const std::string source = rig.folder.File("ones.bin");
        WriteFile(source, std::string(1536 * 1024, '\x01')); // "1," per byte: fits a 4 MiB list frame
        Check(hsm_sensor_add_file_from_path(sensor, source.c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] {
                  rig.Advance(1000);
                  for (const auto& text : rig.server->Received())
                      if (text.find("\"op\":\"list\"") != std::string::npos && text.find("\"Type\":6") != std::string::npos)
                          return true;
                  return false;
              }),
              "the file went back to a list frame" + rig.log.All());
        Check(rig.server->SideRefusedUpgrades() >= 1 && rig.script->Begins().empty(), "the file connection was refused, nothing begun");
        hsm_sensor_release(sensor);
    }

    // `secure` travels in every begin: true by default, false when the options say so.
    void NativeLargeFileSecureTravelsInBegin()
    {
        Rig rig;
        WriteFile(rig.folder.File("a.dmp"), Content(100, 1));
        WriteFile(rig.folder.File("b.dmp"), Content(100, 2));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* first = nullptr;
        hsm_large_file_t* second = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", rig.folder.File("a.dmp").c_str(), &options, &first) == HSM_RESULT_OK, "send a");
        options.not_secure = 1;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", rig.folder.File("b.dmp").c_str(), &options, &second) == HSM_RESULT_OK, "send b");
        Check(WaitFor([&] { return Settled(first) && Settled(second); }), "settled");
        // Found by name: a file that waited for the session may go after the other.
        const auto begins = rig.script->Begins();
        std::string a_raw, b_raw;
        for (const auto& begin : begins)
            (begin.name == "a.dmp" ? a_raw : b_raw) = begin.raw;
        Check(begins.size() == 2 && a_raw.find("\"secure\":true") != std::string::npos && b_raw.find("\"secure\":false") != std::string::npos,
              "secure by default, not when the options say so");
        options.not_secure = 2;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", "x", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "not_secure is 0 or 1");
        hsm_large_file_release(first);
        hsm_large_file_release(second);
    }

    void NativeLargeFileEmptyFileIsStored()
    {
        Rig rig;
        const std::string source = rig.folder.File("empty.dmp");
        WriteFile(source, std::string());
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
        Check(rig.script->Pieces().empty() && rig.script->Ends().size() == 1, "no piece, one end");
        hsm_large_file_release(file);
    }

    void NativeLargeFileHttpTransportIsUnavailable()
    {
        Rig rig;
        rig.Destroy();
        rig.Make(/*session=*/false);
        Check(hsm_collector_use_http_transport(rig.collector) == HSM_RESULT_OK, "http");
        const std::string source = rig.folder.File("http.dmp");
        WriteFile(source, Content(10, 1));
        auto options = NoLimit();
        hsm_large_file_watch_t* watch = nullptr;
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &options, &watch) == HSM_RESULT_OK,
              "the call does not fail for the transport");
        hsm_large_file_t* file = nullptr;
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "nor does the send");
        Check(Result(file).outcome == HSM_LARGE_FILE_UNAVAILABLE && Reason(file).find("session transport") != std::string::npos, "UNAVAILABLE with the reason");
        hsm_large_file_watch_stats_t stats{};
        stats.struct_size = sizeof stats;
        hsm_large_file_watch_get_stats(watch, &stats);
        Check(stats.state == HSM_LARGE_FILE_WATCH_UNAVAILABLE, "the watch is UNAVAILABLE");
        Check(rig.log.Count("Large files cannot be sent") == 1, "said once");
        Check(Exists(source), "nothing touched");
        hsm_large_file_watch_release(watch);
        hsm_large_file_release(file);
    }

    void NativeLargeFileArgumentMistakesAndDispose()
    {
        Rig rig;
        auto options = hsm_large_file_options_default();
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "[a].dmp", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "a bad mask");
        Check(hsm_collector_watch_large_files(rig.collector, "", rig.folder.Str().c_str(), "*", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "no sensor path");
        options.settle_ms = 10;
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "settle below 1 s");
        options = hsm_large_file_options_default();
        options.max_bytes_per_second = -2;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", "x", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "a negative limit");
        options = hsm_large_file_options_default();
        options.struct_size = 0;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", "x", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "struct_size 0");
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*", nullptr, nullptr) == HSM_RESULT_OK, "a watch");
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*", nullptr, nullptr) == HSM_RESULT_INVALID_ARGUMENT, "an equal watch");

        // Pending when the collector is disposed: cancelled, said and counted.
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", rig.folder.File("never.dmp").c_str(), nullptr, &file) == HSM_RESULT_OK, "a pending file");
        hsm_collector_dispose(rig.collector);
        Check(Result(file).outcome == HSM_LARGE_FILE_CANCELLED, "cancelled at dispose");
        Check(rig.log.Count("handed-over large file(s) were not sent") == 1, "said");
        Check(hsm_collector_send_large_file(rig.collector, "dumps", "x", nullptr, nullptr) == HSM_RESULT_INVALID_STATE, "disposed");
        Check(hsm_collector_set_data_folder(rig.collector, nullptr) == HSM_RESULT_INVALID_STATE, "disposed");
        rig.Destroy();
        Check(Result(file).outcome == HSM_LARGE_FILE_CANCELLED, "the handle outlives the collector");
        hsm_large_file_cancel(file);
        hsm_large_file_release(file);
    }

    // Stop, cancel and destroy in the middle of a piece, many times (run under the sanitizers).
    void NativeLargeFileStopAndDisposeInTheMiddle()
    {
        for (int round = 0; round < 20; ++round)
        {
            Rig rig;
            const std::string source = rig.folder.File("mid.dmp");
            WriteFile(source, Content(3 * 65536, 8));
            rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer());
            rig.Start();
            auto options = NoLimit();
            hsm_large_file_t* file = nullptr;
            Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
            Check(WaitFor([&] { return rig.script->Pieces().size() == 1; }), "a piece in flight");
            const auto began = std::chrono::steady_clock::now();
            switch (round % 4)
            {
            case 0:
                Check(hsm_collector_stop(rig.collector) == HSM_RESULT_OK, "stop");
                break;
            case 1:
                hsm_collector_dispose(rig.collector);
                break;
            case 2:
                hsm_large_file_cancel(file);
                Check(WaitFor([&] { return Result(file).outcome == HSM_LARGE_FILE_CANCELLED; }), "cancelled");
                break;
            default: {
                std::thread other([&] { rig.Destroy(); });
                other.join();
                break;
            }
            }
            Check(std::chrono::steady_clock::now() - began < std::chrono::seconds(4), "never waits for the upload");
            Check(Exists(source), "the source stays");
            hsm_large_file_release(file);
        }
    }

    // ---- The review of #1912 ----

    bool ListFrameWithName(Rig& rig, const std::string& name)
    {
        for (const auto& text : rig.server->Received())
            if (text.find("\"op\":\"list\"") != std::string::npos && text.find("\"Type\":6") != std::string::npos &&
                text.find("\"Name\":\"" + name + "\"") != std::string::npos)
                return true;
        return false;
    }

    // B2: end answered 202, then 409 "pieces are missing" (the store lost pieces during the check). The server
    // let the upload go when the check began, so the client begins again and goes on from the piece begin
    // names; a piece sent straight on would be a large frame without an upload (1002).
    void NativeLargeFileMissingAfterCheckingBeginsAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("lost.dmp");
        const std::string bytes = Content(3 * 65536, 12);
        WriteFile(source, bytes);
        rig.script->SetCheckingRounds(1);
        rig.script->SetCheckMissingFrom(1);
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file) + rig.log.All());
        const auto ends = rig.script->Ends();
        Check(ends.size() == 3 && ends[0].status == 202 && ends[1].status == 409 && ends[2].status == 200, "end: 202, 409, then stored");
        Check(rig.script->Begins().size() == 2, "begun again after the 409");
        std::vector<std::int64_t> numbers;
        for (const auto& piece : rig.script->Pieces())
            numbers.push_back(piece.number);
        Check(numbers == std::vector<std::int64_t>{ 0, 1, 2, 1, 2 }, "went on from the piece begin named");
        for (const auto& piece : rig.script->Pieces())
            Check(piece.status == 200, "no piece without an upload");
        Check(rig.log.Count("1002") == 0, "no protocol close");
        Check(rig.script->StoredFiles().begin()->second == bytes, "the bytes");
        hsm_large_file_release(file);
    }

    // B3: the comment of a begin is bounded in UTF-16 code units (the server's characters), the ellipsis
    // included, and never cut inside a code point; path and name are bounded at 1,024.
    void NativeLargeFileBeginTextIsBounded()
    {
        Check(lf::FileConnection::Utf16Length("abc") == 3 && lf::FileConnection::Utf16Length("\xC3\xA9") == 1 &&
                  lf::FileConnection::Utf16Length("\xE2\x80\xA6") == 1 && lf::FileConnection::Utf16Length("\xF0\x9F\x98\x80") == 2,
              "UTF-16 code units: 1 below U+10000, 2 above");
        const std::string ellipsis = "\xE2\x80\xA6";
        const std::string fits(4096, 'a');
        Check(lf::FileConnection::BoundComment(fits) == fits, "4,096 characters fit");
        const std::string cut = lf::FileConnection::BoundComment(std::string(5000, 'a'));
        Check(cut == std::string(4095, 'a') + ellipsis && lf::FileConnection::Utf16Length(cut) == 4096, "cut to 4,095 and an ellipsis");
        std::string wide;
        for (int i = 0; i < 2100; ++i)
            wide += "\xF0\x9F\x98\x80"; // 4,200 units
        const std::string wide_cut = lf::FileConnection::BoundComment(wide);
        Check(lf::FileConnection::Utf16Length(wide_cut) <= 4096 && wide_cut.size() == 2047 * 4 + 3 && wide_cut.substr(wide_cut.size() - 3) == ellipsis,
              "whole code points above U+FFFF (two units each), then the ellipsis");
        std::string mixed = "a";
        for (int i = 0; i < 2048; ++i)
            mixed += "\xF0\x9F\x98\x80"; // 4,097 units: the last one does not fit with the ellipsis
        const std::string mixed_cut = lf::FileConnection::BoundComment(mixed);
        Check(mixed_cut == "a" + wide.substr(0, 2047 * 4) + ellipsis && lf::FileConnection::Utf16Length(mixed_cut) == 4096, "no cut inside a code point");

        Rig rig;
        auto options = NoLimit();
        const std::string long_path(1100, 'p');
        Check(hsm_collector_watch_large_files(rig.collector, long_path.c_str(), rig.folder.Str().c_str(), "*", &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT,
              "a watch of a sensor path over 1,024 is refused at the call");
        Check(hsm_collector_send_large_file(rig.collector, long_path.c_str(), rig.folder.File("a.dmp").c_str(), &options, nullptr) == HSM_RESULT_INVALID_ARGUMENT,
              "so is a hand-over");
        Check(std::string(hsm_collector_last_error(rig.collector)).find("1024") != std::string::npos, "with the reason");

        // A name over 1,024 characters (no file system has one): a final refusal of that file.
        rig.Start();
        hsm_large_file_t* file = nullptr;
        const std::string long_name = rig.folder.File(std::string(1030, 'n') + ".dmp");
        Check(hsm_collector_send_large_file(rig.collector, "dumps", long_name.c_str(), &options, &file) == HSM_RESULT_OK, "a long name is handed over");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_REFUSED && Reason(file).find("1024") != std::string::npos,
              "and refused for good: " + Reason(file));
        Check(rig.script->Begins().empty(), "no begin");
        hsm_large_file_release(file);

        // A routed send whose name is over 1,024 characters takes the ordinary path.
        hsm_sensor_t* sensor = nullptr;
        const std::string sensor_name(1030, 'r');
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", sensor_name.c_str(), "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return ListFrameWithName(rig, sensor_name); }), "a list frame" + rig.log.All());
        Check(rig.script->Begins().empty(), "not on the file connection");
        hsm_sensor_release(sensor);
    }

    // S2: a routed send whose comment is not UTF-8 cannot be a begin: the ordinary path.
    void NativeLargeFileRoutedBadCommentTakesTodaysPath()
    {
        Rig rig;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "latin", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, "caf\xE9") == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return ListFrameWithName(rig, "latin"); }), "a list frame" + rig.log.All());
        Check(rig.script->Begins().empty(), "not on the file connection");
        hsm_sensor_release(sensor);
    }

#if !defined(_WIN32)
    // S2: a handed-over path whose name is not UTF-8 (POSIX) is refused for good, not retried.
    void NativeLargeFileBadNameIsRefused()
    {
        Rig rig;
        const std::string source = rig.folder.Str() + "/bad\xFF.dmp";
        WriteFile(source, Content(1000, 1));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_REFUSED && Reason(file).find("UTF-8") != std::string::npos,
              "refused: " + Reason(file));
        Check(rig.script->Begins().empty(), "no begin");
        hsm_large_file_release(file);
    }
#endif

    // S5: a piece answered with a 4xx other than 400, 404 and 409 is a final refusal.
    void NativeLargeFilePieceRefusalIsFinal()
    {
        Rig rig;
        const std::string source = rig.folder.File("forbidden.dmp");
        WriteFile(source, Content(70000, 1));
        rig.script->AnswerPieceNext(FileScriptAnswer::WithStatus(403, -1, std::string(), "scripted"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_REFUSED, "refused: " + Reason(file));
        Check(rig.script->Begins().size() == 1 && rig.script->Pieces().size() == 1, "not tried again");
        Check(Exists(source), "left where it is");
        hsm_large_file_release(file);
    }

    // 404 on a piece or at end: backed off and begun again; past three such restarts the file is given up
    // (persisted), and a routed send is dropped and counted.
    void NativeLargeFileNotHeldIsBounded()
    {
        Rig rig;
        const std::string source = rig.folder.File("lease.dmp");
        WriteFile(source, Content(70000, 2));
        for (int i = 0; i < 4; ++i)
            rig.script->AnswerPieceNext(FileScriptAnswer::WithStatus(404, -1, std::string(), "scripted"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_GIVEN_UP, "given up: " + Reason(file));
        Check(rig.script->Begins().size() == 4, "begun again after each of the first three");
        int backoffs = 0;
        for (const auto& wait : rig.waits.Copy())
            backoffs += wait.first == "the server lost the upload" ? 1 : 0;
        Check(backoffs == 3, "a backoff before each begin again: " + std::to_string(backoffs));
        hsm_large_file_release(file);
        rig.Restart();
        rig.Start();
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "again after a restart");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_GIVEN_UP, "the state remembers it");
        hsm_large_file_release(file);

        // A routed send.
        Rig routed;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(routed.collector, "reports", "lease", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        for (int i = 0; i < 4; ++i)
            routed.script->AnswerPieceNext(FileScriptAnswer::WithStatus(404, -1, std::string(), "scripted"));
        routed.Start();
        Check(WaitFor([&] { return routed.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x02').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return routed.log.Count("was dropped: the server lost its upload") == 1; }), "dropped and said" + routed.log.All());
        Check(routed.script->Begins().size() == 4 && routed.script->StoredFiles().empty(), "four begins, nothing stored");
        hsm_sensor_release(sensor);
    }

    // S1: a cancel that races the `stored` answer. Before the outcome is committed: the source is kept and the
    // outcome stays CANCELLED; after the delete: STORED wins.
    void NativeLargeFileCancelRacingStored()
    {
        for (int round = 0; round < 2; ++round)
        {
            Rig rig;
            const std::string source = rig.folder.File("race.dmp");
            WriteFile(source, Content(1000, 3));
            std::atomic<hsm_large_file_t*> handle{ nullptr };
            const std::string phase = round == 0 ? "stored" : "deleted";
            lf::test::OnFinish(rig.collector, [&handle, phase](const std::string& now) {
                if (now == phase && handle.load() != nullptr)
                    hsm_large_file_cancel(handle.load());
            });
            rig.Start();
            auto options = NoLimit();
            hsm_large_file_t* file = nullptr;
            Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
            handle.store(file);
            Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }), "the server stored it");
            // The uploader settles the outcome after the hook: wait for its last line.
            Check(WaitFor([&] { return rig.log.Count("is stored on") >= 1; }), "the uploader said it" + rig.log.All());
            const auto result = Result(file);
            if (round == 0)
                Check(result.outcome == HSM_LARGE_FILE_CANCELLED && Exists(source) && !result.source_deleted, "cancelled first: kept, CANCELLED");
            else
                Check(result.outcome == HSM_LARGE_FILE_STORED && !Exists(source) && result.source_deleted, "deleted first: STORED");
            hsm_large_file_release(file);
        }
    }

    // S7(a): a sensor whose registration the server refused is not acknowledged: its routed value takes the
    // ordinary path at once.
    void NativeLargeFileRefusedRegistrationTakesTodaysPath()
    {
        Rig rig;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "refused", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.server->AckNextCommandFramesWith({ 422 });
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the registration was posted");
        Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return ListFrameWithName(rig, "refused"); }), "a list frame" + rig.log.All());
        Check(rig.script->Begins().empty(), "no begin for a refused sensor");
        hsm_sensor_release(sensor);
    }

    // S7(c): the threshold is a quarter of the frame limit of the session the value is sent on.
    void NativeLargeFileThresholdFollowsTheSession()
    {
        Rig rig;
        rig.server->SetHello(1024 * 1024, 30);
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "quarter", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        // 300,000 bytes: above 1 MiB / 4, far below 4 MiB / 4.
        Check(hsm_sensor_add_file(sensor, std::string(300000, '\x03').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }), "routed by this session's frame limit" + rig.log.All());
        hsm_sensor_release(sensor);
    }

    // S7(d): an answer that is not final and carries no retryAfter waits 30 s.
    void NativeLargeFileDefaultRetryIsThirtySeconds()
    {
        Rig rig;
        const std::string source = rig.folder.File("wait.dmp");
        WriteFile(source, Content(1000, 4));
        rig.script->AnswerBeginNext(FileScriptAnswer::WithStatus(202, -1, "{\"state\":\"checking\"}"));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
        bool waited = false;
        for (const auto& wait : rig.waits.Copy())
            waited = waited || (wait.first == "begin: the server checks the file" && wait.second == 30000);
        Check(waited, "waited 30 s");
        hsm_large_file_release(file);
    }

    // S6: a routed send no begin was answered for goes back to the value queue at Stop, before its drain.
    void NativeLargeFileStopGivesRoutedSendsBack()
    {
        Rig rig;
        rig.server->RefuseSideUpgradesWith(503); // the file connection cannot be opened: nothing begins
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "back", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.waits.idle_advances.store(false);
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
        Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.server->SideRefusedUpgrades() >= 1; }), "the route was tried");
        Check(!ListFrameWithName(rig, "back"), "not sent yet");
        Check(hsm_collector_stop(rig.collector) == HSM_RESULT_OK, "stop");
        Check(ListFrameWithName(rig, "back"), "the stop drain sent it the ordinary way" + rig.log.All());
        Check(rig.log.Count("was dropped") == 0, "nothing dropped");
        hsm_sensor_release(sensor);
    }

    // S4: after a 1008 the routed sends already queued take the ordinary path at once.
    void NativeLargeFileClose1008SendsRoutedBack()
    {
        Rig rig;
        hsm_sensor_t* first = nullptr;
        hsm_sensor_t* second = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "first", "bin", &first) == HSM_RESULT_OK, "file sensor");
        Check(hsm_collector_create_file_sensor(rig.collector, "reports2", "second", "bin", &second) == HSM_RESULT_OK, "file sensor");
        rig.script->AnswerPieceNext(FileScriptAnswer::CloseWith(1008, "the access key can no longer send"));
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports2") >= 1; }), "the sensors are registered");
        Check(hsm_sensor_add_file(first, std::string(1536 * 1024, '\x02').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(hsm_sensor_add_file(second, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return ListFrameWithName(rig, "second"); }), "the second went the ordinary way" + rig.log.All());
        Check(rig.log.Count("first.bin") >= 1 && rig.log.Count("was dropped: the access key may no longer send") == 1, "the first was dropped and said");
        Check(rig.script->Begins().size() == 1, "one begin only");
        hsm_sensor_release(first);
        hsm_sensor_release(second);
    }

    // S7(b): routed sends wait in memory bounded in bytes (64 MiB); one is always taken; the overflow is
    // dropped and counted. And S6 at the level of the uploader: a stop gives back what no begin was answered for.
    void NativeLargeFileRoutedSendsAreBoundedInBytes()
    {
        lf::LargeFiles files;
        std::atomic<int> dropped{ 0 };
        std::atomic<int> back{ 0 };
        std::mutex lines_mutex;
        std::vector<std::string> lines;
        lf::RunContext context;
        context.now_ms = [] { return std::int64_t{ 1000 }; };
        context.report = [&](bool, const std::string& message) {
            std::lock_guard<std::mutex> guard(lines_mutex);
            lines.push_back(message);
        };
        context.count_lost_file = [] {};
        context.count_dropped_value = [&] { ++dropped; };
        context.session = [] { return hsm::http::SessionIdentity{}; }; // no session: nothing begins
        context.commands = [](const std::string&, const std::atomic<bool>&) { return hsm::http::SessionResult{}; };
        context.wake_session = [] {};
        files.Start(std::move(context));

        const auto routed = [&](std::shared_ptr<const std::string> content) {
            auto file = std::make_shared<lf::RoutedFile>();
            file->sensor = "host/module/reports";
            file->name = "r.bin";
            file->content = std::move(content);
            file->registration = [] { return lf::RoutedRegistration::Pending; };
            file->back_to_queue = [&](const lf::RoutedFile&) { ++back; };
            return file;
        };
        const auto huge = std::make_shared<const std::string>(65 * 1024 * 1024, 'h');
        const auto ten = std::make_shared<const std::string>(10 * 1024 * 1024, 't');
        Check(files.Route(routed(huge)) == lf::RouteResult::Taken, "an empty queue takes any size");
        Check(files.Route(routed(ten)) == lf::RouteResult::NotRouted, "past 64 MiB: today's path");
        Check(files.Route(routed(ten)) == lf::RouteResult::NotRouted, "again: today's path");
        Check(dropped.load() == 0, "nothing is dropped by the bound");
        const auto said = [&] {
            std::lock_guard<std::mutex> guard(lines_mutex);
            return std::count_if(lines.begin(), lines.end(), [](const std::string& line) { return line.find("wait for the file connection already") != std::string::npos; });
        };
        Check(said() == 1, "the bound is said once while it holds");
        files.Stop();
        Check(back.load() == 1, "the stop gives back the one no begin was answered for");
        Check(files.Route(routed(ten)) == lf::RouteResult::NotRouted, "not running: today's path");
    }

    // Past the bound of routed sends waiting in memory a file value takes today's path: a list frame when it
    // fits. The bound is lowered through a seam; the file connection cannot be opened (503), so the first
    // routed send waits.
    void NativeLargeFileRoutedBoundFullTakesTodaysPath()
    {
        Rig rig;
        rig.server->RefuseSideUpgradesWith(503);
        lf::test::Of(rig.collector)->max_routed_bytes_for_tests.store(2 * 1024 * 1024);
        hsm_sensor_t* first = nullptr;
        hsm_sensor_t* second = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "waiting", "bin", &first) == HSM_RESULT_OK, "file sensor");
        Check(hsm_collector_create_file_sensor(rig.collector, "reports2", "overflow", "bin", &second) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        Check(WaitFor([&] { return rig.Registrations("host/module/reports2") >= 1; }), "the sensors are registered");
        Check(hsm_sensor_add_file(first, std::string(1536 * 1024, '\x05').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.server->SideRefusedUpgrades() >= 1; }), "the first one is routed and waits");
        Check(hsm_sensor_add_file(second, std::string(1536 * 1024, '\x01').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return ListFrameWithName(rig, "overflow"); }), "past the bound: a list frame" + rig.log.All());
        Check(!ListFrameWithName(rig, "waiting"), "the waiting one is still routed");
        Check(rig.log.Count("wait for the file connection already") == 1, "the bound is said once");
        Check(rig.log.Count("was dropped") == 0, "nothing dropped");
        hsm_sensor_release(first);
        hsm_sensor_release(second);
    }

    // A file value is held once: only a value that some session could route (above 16,384 bytes, a quarter of
    // the smallest frame a server announces) keeps a route copy of its content beside its JSON.
    void NativeLargeFileSmallFileValueHasNoRouteCopy()
    {
        Rig rig;
        hsm_sensor_t* sensor = nullptr;
        Check(hsm_collector_create_file_sensor(rig.collector, "reports", "small", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
        rig.Start();
        Check(hsm_sensor_add_file(sensor, std::string(16384, 'a').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(lf::test::RouteCopies(rig.collector) == 0, "16,384 bytes: no route copy");
        Check(hsm_sensor_add_file(sensor, std::string(16385, 'a').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
        Check(lf::test::RouteCopies(rig.collector) == 1, "16,385 bytes: a route copy");
        hsm_sensor_release(sensor);
    }

    // A wait of the uploader held (the clock does not move for it) while the collector is torn down from
    // another thread; the test thread keeps reading and cancelling the handle meanwhile and joins last.
    void TeardownDuringWait(const std::string& wait, int teardown)
    {
        Rig rig;
        rig.waits.hold = wait;
        const std::string source = rig.folder.File("wait.dmp");
        WriteFile(source, Content(3 * 65536, 13));
        auto options = NoLimit();
        if (wait == "the speed limit")
            options.max_bytes_per_second = 1000;
        else if (wait == "a piece: the server could not write it")
            rig.script->AnswerPieceNext(FileScriptAnswer::WithStatus(503, 60, std::string(), "no room"));
        else
            rig.script->SetCheckingRounds(1);
        rig.Start();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return rig.waits.held.load(); }), "the uploader waits: " + wait + rig.log.All());
        const auto began = std::chrono::steady_clock::now();
        std::atomic<bool> done{ false };
        std::thread other([&] {
            switch (teardown)
            {
            case 0:
                hsm_collector_stop(rig.collector);
                break;
            case 1:
                hsm_collector_dispose(rig.collector);
                break;
            default:
                rig.Destroy();
                break;
            }
            done.store(true);
        });
        while (!done.load())
        {
            Result(file);
            Reason(file);
        }
        other.join();
        Check(std::chrono::steady_clock::now() - began < std::chrono::seconds(10), "the teardown did not wait for the wait");
        Check(Exists(source), "the source stays");
        hsm_large_file_cancel(file);
        hsm_large_file_release(file);
    }

    void NativeLargeFileTeardownDuringWaits()
    {
        for (const char* wait : { "a piece: the server could not write it", "end: the server checks the file", "the speed limit" })
            for (int teardown = 0; teardown < 3; ++teardown)
                TeardownDuringWait(wait, teardown);
    }

    // hsm_large_file_watch_release from another thread while the watch's file is uploaded, and racing its
    // completion; the collector goes on with other files.
    void NativeLargeFileWatchReleaseRacingAnUpload()
    {
        for (int round = 0; round < 6; ++round)
        {
            Rig rig;
            const std::string watched = rig.folder.File("w.dmp");
            WriteFile(watched, Content(3 * 65536, 20 + round));
            if (round % 2 == 0)
                rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer()); // released in the middle
            auto options = NoLimit();
            options.keep_source = 1;
            hsm_large_file_watch_t* watch = nullptr;
            Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "w.dmp", &options, &watch) == HSM_RESULT_OK, "watch");
            rig.StartDriven();
            rig.Settle(60000);
            Check(WaitFor([&] { return rig.script->Pieces().size() >= 1; }), "uploading");
            std::thread other([&] { hsm_large_file_watch_release(watch); });
            other.join();
            // Another file afterwards: the uploader goes on.
            const std::string next = rig.folder.File("next.dmp");
            WriteFile(next, Content(1000, 1));
            hsm_large_file_t* file = nullptr;
            auto plain = NoLimit();
            Check(hsm_collector_send_large_file(rig.collector, "dumps", next.c_str(), &plain, &file) == HSM_RESULT_OK, "send");
            Check(WaitFor([&] {
                      rig.Advance(1000);
                      return Settled(file);
                  }),
                  "the next file settles" + rig.log.All());
            Check(Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
            Check(Exists(watched), "the watched file stays");
            hsm_large_file_release(file);
        }
    }

    // The logger on the uploader thread calls stop and dispose: both are refused (the dispose said once) and
    // nothing deadlocks; the upload goes on.
    void NativeLargeFileLifecycleFromTheLoggerIsRefused()
    {
        // Declared before the rig: the logger reads it until the collector is destroyed.
        struct Calls
        {
            hsm_collector_t* collector = nullptr;
            std::atomic<bool> called{ false };
            std::atomic<int> stop_result{ -1 };
            LogLines* log = nullptr;
        } calls;
        Rig rig;
        calls.collector = rig.collector;
        calls.log = &rig.log;
        hsm_collector_set_logger(
            rig.collector,
            [](hsm_log_level_t, const char* message, void* user) {
                auto* self = static_cast<Calls*>(user);
                const std::string text = message != nullptr ? message : "";
                {
                    std::lock_guard<std::mutex> guard(self->log->mutex);
                    self->log->lines.push_back(text);
                }
                if (text.find("begin on") != std::string::npos && !self->called.exchange(true))
                {
                    self->stop_result.store(static_cast<int>(hsm_collector_stop(self->collector)));
                    hsm_collector_dispose(self->collector);
                }
            },
            &calls);
        const std::string source = rig.folder.File("logger.dmp");
        WriteFile(source, Content(1000, 1));
        rig.Start();
        auto options = NoLimit();
        hsm_large_file_t* file = nullptr;
        Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
        Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: no deadlock" + rig.log.All());
        Check(calls.stop_result.load() == HSM_RESULT_INVALID_STATE, "stop on the uploader thread is refused");
        Check(rig.log.Count("Dispose was called on the large file uploader's thread") == 1, "dispose is refused and said once");
        Check(hsm_collector_status(rig.collector) == HSM_COLLECTOR_STATUS_RUNNING, "the collector still runs");
        hsm_large_file_release(file);
    }

    // A record of an upload cut by a stop, whose file changed before the next run: that upload is discarded
    // (begin with the facts it was begun with, then discard) and the new file sent; against a server without
    // the op (400 "unknown op") it is said once and the part left to expire.
    void NativeLargeFileDiscardAfterACut()
    {
        for (int round = 0; round < 2; ++round)
        {
            Rig rig;
            const std::string source = rig.folder.File("cut.dmp");
            WriteFile(source, Content(3 * 65536, 40));
            rig.script->AnswerPieceNext(FileScriptAnswer::Default());
            rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer());
            rig.Start();
            auto options = NoLimit();
            hsm_large_file_t* file = nullptr;
            Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
            Check(WaitFor([&] { return rig.script->Pieces().size() == 2; }), "a piece in flight");
            Check(hsm_collector_stop(rig.collector) == HSM_RESULT_OK, "stop");
            hsm_large_file_release(file);
            rig.Destroy();
            WriteFile(source, Content(2 * 65536, 41)); // another file under the same name
            if (round == 1)
                rig.script->SetDiscardUnknown(true);
            rig.Make();
            rig.Start();
            Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send again");
            Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "the new file is stored: " + Reason(file) + rig.log.All());
            const auto discards = rig.script->Discards();
            Check(!discards.empty(), "the cut upload was discarded");
            if (round == 0)
                Check(discards.back().status == 200, "discarded");
            else
                Check(rig.log.Count("The server has no discard") == 1, "an older server: said once");
            Check(rig.script->StoredFiles().begin()->second.size() == 2 * 65536, "the new content");
            hsm_large_file_release(file);
        }
    }

    // Design §17 (with #1910's bounded sends): a piece whose write cannot finish (the server stops reading
    // after the begin) ends at its send bound without an answer: the connection is lost, not closed by
    // the server, so it counts as neither a checksum mismatch nor a protocol close. The file is sent
    // again on a new connection after a begin and stored. And the ack deadline starts once the frame is
    // written: a write that takes longer than the ack timeout is still answered in time.
    void NativeLargeFilePieceSendTimeoutBeginsAgain()
    {
        constexpr std::int64_t kBigFrame = 32LL * 1024 * 1024; // a piece larger than the loopback buffers
        for (int round = 0; round < 2; ++round)
        {
            Rig rig;
            rig.script->SetHello(kBigFrame, 30, 1LL << 40);
            const auto& script = rig.script;
            rig.server->PauseFileReading(1, 2); // the probe and the begin, then nothing
            FileServerScript* scripted = script.get();
            lf::test::AdjustConnection(rig.collector, [scripted, round](lf::FileConnectionOptions& connection) {
                connection.ack_abandoned_for_tests = [scripted](std::int64_t id) { return scripted->Unanswered(id); };
                if (round == 0)
                    // A short bound only on the first connection, the one that stops reading.
                    connection.send_bound_ms_for_tests = [](std::int64_t opened) { return opened == 1 ? std::int64_t{ 1000 } : std::int64_t{ 600000 }; };
                else
                    connection.request_timeout_ms = 8000; // the ack deadline, below how long the write is held
            });
            const std::string source = rig.folder.File("stuck.dmp");
            const std::string bytes = Content(static_cast<std::size_t>(script->PieceBytes()) + 1000, 50 + round); // two pieces
            WriteFile(source, bytes);
            rig.Start();
            auto options = NoLimit();
            hsm_large_file_t* file = nullptr;
            Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
            if (round == 1)
            {
                // The write of the first piece is held well past the ack deadline, then the server reads on.
                Check(WaitFor([&] { return rig.server->PausedFileBytes() > 0; }), "the piece's write is held" + rig.log.All());
                std::this_thread::sleep_for(std::chrono::milliseconds(12000));
                rig.server->ResumeFileReading();
            }
            Check(WaitFor([&] { return Settled(file); }, 60) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored (round " + std::to_string(round) + "): " + Reason(file) + rig.log.All());
            Check(rig.script->StoredFiles().begin()->second == bytes, "the bytes");
            std::vector<std::int64_t> numbers;
            for (const auto& piece : rig.script->Pieces())
                numbers.push_back(piece.number);
            Check(numbers == std::vector<std::int64_t>{ 0, 1 }, "each piece stored once (the cut one never reached the script)");
            Check(rig.log.Count("checksum") == 0 && rig.log.Count("closed the file connection with") == 0, "neither a checksum mismatch nor a protocol close");
            if (round == 0)
            {
                Check(rig.script->Begins().size() == 2 && rig.script->Connections().size() >= 2, "a new connection and a begin");
                Check(rig.log.Count("send timed out") >= 1, "the write ended at its bound: " + rig.log.All());
            }
            else
                Check(rig.script->Begins().size() == 1, "the ack came after the long write: no new connection" + rig.log.All());
            rig.server->ResumeFileReading();
            hsm_large_file_release(file);
        }
    }

    // `end` answered 503 (the server could not read the part): wait, begin, end again, at most three such
    // rounds in a row; then a long wait (30 minutes), said once and not counted as a loss, and the file is
    // sent after it. Both paths: a handed-over file and a routed send.
    void NativeLargeFileEndUnreadableIsBounded()
    {
        for (int round = 0; round < 2; ++round)
        {
            Rig rig;
            hsm_sensor_t* sensor = nullptr;
            if (round == 1)
                Check(hsm_collector_create_file_sensor(rig.collector, "reports", "unread", "bin", &sensor) == HSM_RESULT_OK, "file sensor");
            for (int i = 0; i < 4; ++i)
                rig.script->AnswerEndNext(FileScriptAnswer::WithStatus(503, 1, std::string(), "the part cannot be read"));
            rig.StartDriven(); // the clock moves only by the uploader's waits other than the idle one
            hsm_large_file_t* file = nullptr;
            if (round == 0)
            {
                const std::string source = rig.folder.File("unread.dmp");
                WriteFile(source, Content(70000, 60));
                auto options = NoLimit();
                Check(hsm_collector_send_large_file(rig.collector, "dumps", source.c_str(), &options, &file) == HSM_RESULT_OK, "send");
            }
            else
            {
                Check(WaitFor([&] { return rig.Registrations("host/module/reports") >= 1; }), "the sensor is registered");
                Check(hsm_sensor_add_file(sensor, std::string(1536 * 1024, '\x04').c_str(), HSM_SENSOR_STATUS_OK, nullptr) == HSM_RESULT_OK, "send");
                // A routed send waits between attempts on the idle clock: moved here, a second at a time.
                Check(WaitFor([&] {
                          rig.Advance(1000);
                          return rig.script->Ends().size() >= 4;
                      }),
                      "four ends" + rig.log.All());
            }
            Check(WaitFor([&] { return rig.log.Count("could not read the stored part") == 1; }), "said once past the bound" + rig.log.All());
            std::this_thread::sleep_for(std::chrono::milliseconds(300));
            Check(rig.script->Ends().size() == 4, "no fifth end before the long wait: " + std::to_string(rig.script->Ends().size()));
            Check(rig.script->Begins().size() == 4, "a begin before each end: " + std::to_string(rig.script->Begins().size()));
            if (round == 0)
                Check(Result(file).outcome == HSM_LARGE_FILE_PENDING, "still pending, not a loss");
            Check(rig.log.Count("was dropped") == 0 && rig.log.Count("is not sent") == 0, "not counted as a loss");
            rig.Advance(30LL * 60 * 1000);
            rig.Scan();
            Check(WaitFor([&] {
                      rig.Advance(1000);
                      return rig.script->StoredFiles().size() == 1;
                  }),
                  "sent after the long wait" + rig.log.All());
            Check(rig.log.Count("could not read the stored part") == 1, "said once");
            if (file != nullptr)
            {
                Check(WaitFor([&] { return Settled(file); }) && Result(file).outcome == HSM_LARGE_FILE_STORED, "stored: " + Reason(file));
                hsm_large_file_release(file);
            }
            if (sensor != nullptr)
                hsm_sensor_release(sensor);
        }
    }

#if defined(_WIN32)
    void NativeLargeFileWriterHoldsTheFile()
    {
        Rig rig;
        const std::string source = rig.folder.File("held.dmp");
        WriteFile(source, Content(1000, 1));
        const std::wstring wide = fs::u8path(source).wstring();
        HANDLE writer = CreateFileW(wide.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        Check(writer != INVALID_HANDLE_VALUE, "a writer");
        auto options = NoLimit();
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &options, nullptr) == HSM_RESULT_OK, "watch");
        rig.StartDriven();
        rig.Settle(60000);
        std::this_thread::sleep_for(std::chrono::milliseconds(300));
        Check(rig.script->Begins().empty(), "not taken while a writer holds it");
        CloseHandle(writer);
        rig.Settle(60000);
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }), "taken once the writer is gone and it settled again");
    }
#else
    // POSIX: a file that changes in the middle is discarded on the server and sent again when it settled.
    void NativeLargeFileChangedFileIsDiscardedAndSentAgain()
    {
        Rig rig;
        const std::string source = rig.folder.File("changing.dmp");
        WriteFile(source, Content(3 * 65536, 6));
        rig.script->AnswerPieceNext(FileScriptAnswer::NoAnswer());
        auto options = NoLimit();
        options.keep_source = 1;
        Check(hsm_collector_watch_large_files(rig.collector, "dumps", rig.folder.Str().c_str(), "*.dmp", &options, nullptr) == HSM_RESULT_OK, "watch");
        rig.StartDriven();
        rig.Settle(60000);
        Check(WaitFor([&] { return rig.script->Pieces().size() == 1; }), "a piece in flight");
        AppendFile(source, Content(10, 1));
        // The unanswered piece ends with the request timeout; the collector then reads on and finds the change.
        // The connection that lost the piece is opened again after a pause on the collector's clock.
        Check(WaitFor(
                  [&] {
                      rig.Advance(1000);
                      // On the new connection the first discard is answered 404 (it holds no upload);
                      // the collector begins with the facts it began with, then discards that upload.
                      const auto discards = rig.script->Discards();
                      return std::any_of(discards.begin(), discards.end(), [](const FileServerScript::DiscardRecord& d) { return d.status == 200; });
                  },
                  30),
              "the upload is discarded" + rig.log.All());
        rig.Settle(60000);
        Check(WaitFor([&] { return rig.script->StoredFiles().size() == 1; }, 30), "sent again once settled");
        Check(rig.script->StoredFiles().begin()->second.size() == 3 * 65536 + 10, "the changed file");
    }
#endif
#endif
} // namespace

const std::map<std::string, std::function<void(const std::string&)>>& LargeFileUploadTests()
{
    static const std::map<std::string, std::function<void(const std::string&)>> tests = {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        { "native_large_file_mask_matches_both_collectors_rule", [](const std::string&) { NativeLargeFileMaskMatchesBothCollectorsRule(); } },
        { "native_large_file_handed_file_is_stored_and_deleted", [](const std::string&) { NativeLargeFileHandedFileIsStoredAndDeleted(); } },
        { "native_large_file_keep_mode_is_not_sent_again", [](const std::string&) { NativeLargeFileKeepModeIsNotSentAgain(); } },
        { "native_large_file_settle_rule_on_a_growing_file", [](const std::string&) { NativeLargeFileSettleRuleOnAGrowingFile(); } },
        { "native_large_file_files_found_at_start_go_oldest_first", [](const std::string&) { NativeLargeFileFilesFoundAtStartGoOldestFirst(); } },
        { "native_large_file_speed_limit_waits_between_frames", [](const std::string&) { NativeLargeFileSpeedLimitWaitsBetweenFrames(); } },
        { "native_large_file_continues_after_a_drop", [](const std::string&) { NativeLargeFileContinuesAfterADrop(); } },
        { "native_large_file_continues_after_a_restart", [](const std::string&) { NativeLargeFileContinuesAfterARestart(); } },
        { "native_large_file_final_refusals_are_final_for_the_run", [](const std::string&) { NativeLargeFileFinalRefusalsAreFinalForTheRun(); } },
        { "native_large_file_no_room_is_tried_again", [](const std::string&) { NativeLargeFileNoRoomIsTriedAgain(); } },
        { "native_large_file_three_discards_give_up", [](const std::string&) { NativeLargeFileThreeDiscardsGiveUp(); } },
        { "native_large_file_close_1008_is_final", [](const std::string&) { NativeLargeFileClose1008IsFinal(); } },
        { "native_large_file_corrupt_state_is_set_aside", [](const std::string&) { NativeLargeFileCorruptStateIsSetAside(); } },
        { "native_large_file_checksum_mismatch_sends_the_piece_again", [](const std::string&) { NativeLargeFileChecksumMismatchSendsThePieceAgain(); } },
        { "native_large_file_unanswered_piece_is_sent_again", [](const std::string&) { NativeLargeFileUnansweredPieceIsSentAgain(); } },
        { "native_large_file_ordinary_send_above_the_threshold_is_routed", [](const std::string&) { NativeLargeFileOrdinarySendAboveTheThresholdIsRouted(); } },
        { "native_large_file_route_missing_takes_todays_path", [](const std::string&) { NativeLargeFileRouteMissingTakesTodaysPath(); } },
        { "native_large_file_secure_travels_in_begin", [](const std::string&) { NativeLargeFileSecureTravelsInBegin(); } },
        { "native_large_file_cpp_wrapper", [](const std::string&) { NativeLargeFileCppWrapper(); } },
        { "native_large_file_empty_file_is_stored", [](const std::string&) { NativeLargeFileEmptyFileIsStored(); } },
        { "native_large_file_http_transport_is_unavailable", [](const std::string&) { NativeLargeFileHttpTransportIsUnavailable(); } },
        { "native_large_file_argument_mistakes_and_dispose", [](const std::string&) { NativeLargeFileArgumentMistakesAndDispose(); } },
        { "native_large_file_stop_and_dispose_in_the_middle", [](const std::string&) { NativeLargeFileStopAndDisposeInTheMiddle(); } },
        { "native_large_file_missing_after_checking_begins_again", [](const std::string&) { NativeLargeFileMissingAfterCheckingBeginsAgain(); } },
        { "native_large_file_begin_text_is_bounded", [](const std::string&) { NativeLargeFileBeginTextIsBounded(); } },
        { "native_large_file_routed_bad_comment_takes_todays_path", [](const std::string&) { NativeLargeFileRoutedBadCommentTakesTodaysPath(); } },
        { "native_large_file_piece_refusal_is_final", [](const std::string&) { NativeLargeFilePieceRefusalIsFinal(); } },
        { "native_large_file_not_held_is_bounded", [](const std::string&) { NativeLargeFileNotHeldIsBounded(); } },
        { "native_large_file_cancel_racing_stored", [](const std::string&) { NativeLargeFileCancelRacingStored(); } },
        { "native_large_file_refused_registration_takes_todays_path", [](const std::string&) { NativeLargeFileRefusedRegistrationTakesTodaysPath(); } },
        { "native_large_file_threshold_follows_the_session", [](const std::string&) { NativeLargeFileThresholdFollowsTheSession(); } },
        { "native_large_file_default_retry_is_thirty_seconds", [](const std::string&) { NativeLargeFileDefaultRetryIsThirtySeconds(); } },
        { "native_large_file_stop_gives_routed_sends_back", [](const std::string&) { NativeLargeFileStopGivesRoutedSendsBack(); } },
        { "native_large_file_close_1008_sends_routed_back", [](const std::string&) { NativeLargeFileClose1008SendsRoutedBack(); } },
        { "native_large_file_routed_sends_are_bounded_in_bytes", [](const std::string&) { NativeLargeFileRoutedSendsAreBoundedInBytes(); } },
        { "native_large_file_teardown_during_waits", [](const std::string&) { NativeLargeFileTeardownDuringWaits(); } },
        { "native_large_file_watch_release_racing_an_upload", [](const std::string&) { NativeLargeFileWatchReleaseRacingAnUpload(); } },
        { "native_large_file_lifecycle_from_the_logger_is_refused", [](const std::string&) { NativeLargeFileLifecycleFromTheLoggerIsRefused(); } },
        { "native_large_file_discard_after_a_cut", [](const std::string&) { NativeLargeFileDiscardAfterACut(); } },
        { "native_large_file_piece_send_timeout_begins_again", [](const std::string&) { NativeLargeFilePieceSendTimeoutBeginsAgain(); } },
        { "native_large_file_end_unreadable_is_bounded", [](const std::string&) { NativeLargeFileEndUnreadableIsBounded(); } },
        { "native_large_file_routed_bound_full_takes_todays_path", [](const std::string&) { NativeLargeFileRoutedBoundFullTakesTodaysPath(); } },
        { "native_large_file_small_file_value_has_no_route_copy", [](const std::string&) { NativeLargeFileSmallFileValueHasNoRouteCopy(); } },
#if defined(_WIN32)
        { "native_large_file_writer_holds_the_file", [](const std::string&) { NativeLargeFileWriterHoldsTheFile(); } },
#else
        { "native_large_file_changed_file_is_discarded_and_sent_again", [](const std::string&) { NativeLargeFileChangedFileIsDiscardedAndSentAgain(); } },
        { "native_large_file_bad_name_is_refused", [](const std::string&) { NativeLargeFileBadNameIsRefused(); } },
#endif
#endif
    };
    return tests;
}
