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
        { "native_large_file_cpp_wrapper", [](const std::string&) { NativeLargeFileCppWrapper(); } },
        { "native_large_file_empty_file_is_stored", [](const std::string&) { NativeLargeFileEmptyFileIsStored(); } },
        { "native_large_file_http_transport_is_unavailable", [](const std::string&) { NativeLargeFileHttpTransportIsUnavailable(); } },
        { "native_large_file_argument_mistakes_and_dispose", [](const std::string&) { NativeLargeFileArgumentMistakesAndDispose(); } },
        { "native_large_file_stop_and_dispose_in_the_middle", [](const std::string&) { NativeLargeFileStopAndDisposeInTheMiddle(); } },
#if defined(_WIN32)
        { "native_large_file_writer_holds_the_file", [](const std::string&) { NativeLargeFileWriterHoldsTheFile(); } },
#else
        { "native_large_file_changed_file_is_discarded_and_sent_again", [](const std::string&) { NativeLargeFileChangedFileIsDiscardedAndSentAgain(); } },
#endif
#endif
    };
    return tests;
}
