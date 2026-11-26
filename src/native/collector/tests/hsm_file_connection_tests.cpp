// The file connection of the native collector (#1887; wire contract "File connection", #1884): FileConnection
// against a real session and the scripted file side of tests/ws_test_server.hpp
// (tests/file_server_script.hpp). The pieces are built here, as the uploader will build them: a piece of the
// source, zstd level 3 after 88 reserved bytes, the SHA-256 of the original bytes.
//
// No test decides by time: a request the script leaves unanswered ends through ack_abandoned_for_tests as
// soon as the script has decided so, and every other wait is for a state; a timeout only fails a test.

#include <functional>
#include <map>
#include <string>

#if defined(HSM_COLLECTOR_LARGE_FILES)
#include "../src/hsm_session_transport.hpp"
#include "../src/hsm_ws_connection.hpp"
#include "../src/large_files/hsm_file_connection.hpp"
#include "../src/large_files/hsm_sha256.hpp"
#include "file_server_script.hpp"
#include "ws_test_server.hpp"

#include <zstd.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

namespace
{
    namespace lf = hsm::collector::large_files;
    using Server = hsm::test::WebSocketTestServer;
    using Script = hsm::test::FileServerScript;
    using Answer = hsm::test::FileScriptAnswer;
    using Kind = lf::FileAnswerKind;

    constexpr std::int64_t kFrameBytes = 256 * 1024;
    constexpr std::int64_t kMaxFileBytes = 64 * 1024 * 1024;
    constexpr std::int64_t kPieceBytes = 65536;
    // 2026-11-25T10:00:00.1234567Z and 2026-11-25T12:00:00Z (the script's stored time), in ticks since 1970.
    constexpr std::int64_t kModifiedTicks = 1795600800LL * 10000000 + 1234567;
    constexpr std::int64_t kStoredTicks = 1795608000LL * 10000000;

    void Check(bool condition, const std::string& message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

    // A wait that only fails a test when it expires; it never decides the outcome.
    template <typename Predicate>
    void WaitOrFail(Predicate predicate, const std::string& what, int seconds = 60)
    {
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(seconds);
        while (!predicate())
        {
            if (std::chrono::steady_clock::now() > deadline)
                throw std::runtime_error("timed out waiting for " + what);
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    }

    bool WebSocketOrSkip(const char* test)
    {
        std::string reason;
        if (hsm::http::WebSocketConnection::Supported(&reason))
            return true;
        std::string required;
#if defined(_MSC_VER)
        char* env = nullptr;
        std::size_t env_length = 0;
        if (_dupenv_s(&env, &env_length, "HSM_REQUIRE_WEBSOCKET") == 0 && env != nullptr)
        {
            required = env;
            std::free(env);
        }
#else
        if (const char* env = std::getenv("HSM_REQUIRE_WEBSOCKET"))
            required = env;
#endif
        Check(required != "1", std::string("this lane requires a libcurl with WebSocket support: ") + reason);
        std::printf("%s: skipped (%s)\n", test, reason.c_str());
        return false;
    }

    std::string KindName(Kind kind)
    {
        switch (kind)
        {
        case Kind::Next:
            return "Next";
        case Kind::Stored:
            return "Stored";
        case Kind::Checking:
            return "Checking";
        case Kind::RetryLater:
            return "RetryLater";
        case Kind::ChecksumMismatch:
            return "ChecksumMismatch";
        case Kind::Missing:
            return "Missing";
        case Kind::Discarded:
            return "Discarded";
        case Kind::DiscardDone:
            return "DiscardDone";
        case Kind::NotHeld:
            return "NotHeld";
        case Kind::Final:
            return "Final";
        case Kind::BadRequest:
            return "BadRequest";
        case Kind::UnknownOp:
            return "UnknownOp";
        case Kind::Fault:
            return "Fault";
        case Kind::Lost:
            return "Lost";
        case Kind::Cancelled:
            return "Cancelled";
        case Kind::NotOpen:
            return "NotOpen";
        }
        return "?";
    }

    void CheckKind(Kind expected, const lf::FileAnswer& answer, const std::string& what)
    {
        Check(answer.kind == expected, what + ": expected " + KindName(expected) + ", got " + KindName(answer.kind) + " (status " + std::to_string(answer.status) +
                                           ", error '" + answer.error + "')");
    }

    // Bytes that compress in places and not in others.
    std::string SourceBytes(std::size_t size)
    {
        std::string bytes(size, '\0');
        std::uint32_t x = 2463534242u;
        for (std::size_t i = 0; i < size; ++i)
        {
            if ((i / 4096) % 2 == 0)
            {
                bytes[i] = static_cast<char>('a' + i % 7);
                continue;
            }
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            bytes[i] = static_cast<char>(x & 0xFF);
        }
        return bytes;
    }

    // Three whole pieces and a short one.
    std::string Source()
    {
        return SourceBytes(static_cast<std::size_t>(3 * kPieceBytes + 12345));
    }

    struct BuiltPiece
    {
        std::vector<std::uint8_t> frame;
        std::uint32_t length = 0;
        lf::Sha256Digest digest{};
    };

    // Piece `number` of `source`: 88 reserved bytes, then one zstd frame of it (level 3).
    BuiltPiece BuildPiece(const std::string& source, std::int64_t piece_bytes, std::int64_t number)
    {
        const std::size_t offset = static_cast<std::size_t>(number * piece_bytes);
        Check(offset <= source.size(), "a piece past the source");
        const std::size_t length = (std::min)(static_cast<std::size_t>(piece_bytes), source.size() - offset);
        BuiltPiece piece;
        piece.length = static_cast<std::uint32_t>(length);
        const std::size_t bound = ZSTD_compressBound(length);
        piece.frame.assign(lf::kPieceHeaderBytes + bound, 0);
        const std::size_t compressed = ZSTD_compress(piece.frame.data() + lf::kPieceHeaderBytes, bound, source.data() + offset, length, 3);
        Check(!ZSTD_isError(compressed), "zstd must compress the piece");
        piece.frame.resize(lf::kPieceHeaderBytes + compressed);
        Check(lf::Sha256Of(source.data() + offset, length, piece.digest), "SHA-256 of the piece");
        return piece;
    }

    std::int64_t PiecesOf(const std::string& source, std::int64_t piece_bytes)
    {
        return (static_cast<std::int64_t>(source.size()) + piece_bytes - 1) / piece_bytes;
    }

    // The hash of an end: the SHA-256 of the pieces' digests in order.
    std::string HashOfDigests(const std::vector<lf::Sha256Digest>& digests)
    {
        std::string all;
        for (const auto& digest : digests)
            all.append(reinterpret_cast<const char*>(digest.data()), digest.size());
        lf::Sha256Digest hash{};
        Check(lf::Sha256Of(all.data(), all.size(), hash), "SHA-256 of the digests");
        return lf::ToHex(hash.data(), hash.size());
    }

    std::string HashOf(const std::string& source, std::int64_t piece_bytes)
    {
        std::vector<lf::Sha256Digest> digests;
        for (std::int64_t n = 0; n < PiecesOf(source, piece_bytes); ++n)
            digests.push_back(BuildPiece(source, piece_bytes, n).digest);
        return HashOfDigests(digests);
    }

    lf::FileFacts Facts(const std::string& source, const std::string& name = "app_20261125.dmp")
    {
        lf::FileFacts facts;
        facts.path = "app/dumps";
        facts.name = name;
        facts.size = static_cast<std::int64_t>(source.size());
        facts.modified_ticks = kModifiedTicks;
        facts.source = "vol:5b2e/file:00a1";
        return facts;
    }

    // A session to a scripted server whose file side is on.
    struct FileRig
    {
        Server server;
        std::shared_ptr<Script> script;
        hsm::http::SessionOptions session_options;
        std::unique_ptr<hsm::http::SessionTransport> session;

        static Server::Options ServerOptions()
        {
            Server::Options options;
            options.session = true;
            options.side_subprotocol = lf::FileConnectionOptions::kSubprotocol;
            return options;
        }

        explicit FileRig(const std::function<void(Script&)>& setup = {})
            : server(ServerOptions()),
              script(std::make_shared<Script>())
        {
            script->SetHello(kFrameBytes, 30, kMaxFileBytes);
            if (setup)
                setup(*script);
            server.EnableFileScript(script);
            session_options.url = "ws://127.0.0.1:" + std::to_string(server.Port()) + "/api/sensors/session";
            session_options.headers = { { "Key", "file-key" }, { "ClientName", "file-client" } };
            session = std::make_unique<hsm::http::SessionTransport>(session_options);
            session->Start();
            WaitOrFail([&] { return !session->CurrentSession().session_id.empty(); }, "the session");
        }

        ~FileRig() { session->Stop(0); }

        FileRig(const FileRig&) = delete;
        FileRig& operator=(const FileRig&) = delete;

        std::string SessionId() const { return session->CurrentSession().session_id; }

        // A request the script leaves unanswered ends as soon as the script has decided so; any other
        // request waits for its ack (a bound of minutes only fails the test).
        lf::FileConnectionOptions Options() const
        {
            lf::FileConnectionOptions options;
            options.url = session_options.url;
            options.headers = session_options.headers;
            options.verify_peer = false;
            options.request_timeout_ms = 600000;
            const std::shared_ptr<Script> scripted = script;
            const auto bound = std::chrono::steady_clock::now() + std::chrono::minutes(5);
            options.ack_abandoned_for_tests = [scripted, bound](std::int64_t id) { return scripted->Unanswered(id) || std::chrono::steady_clock::now() > bound; };
            return options;
        }

        void Open(lf::FileConnection& connection) const
        {
            const auto opened = connection.Open(SessionId());
            Check(opened.ok, "the file connection must open: " + opened.why);
        }
    };

    lf::FileAnswer Begin(lf::FileConnection& connection, const std::string& source, std::int64_t next = 0)
    {
        const auto answer = connection.Begin(Facts(source), kPieceBytes);
        CheckKind(Kind::Next, answer, "begin");
        Check(answer.next == next, "begin: next " + std::to_string(answer.next) + ", expected " + std::to_string(next));
        Check(answer.pieces == PiecesOf(source, kPieceBytes), "begin: pieces " + std::to_string(answer.pieces));
        Check(answer.piece_bytes == kPieceBytes, "begin: pieceBytes " + std::to_string(answer.piece_bytes));
        Check(answer.upload.size() == 32, "begin: an upload id of 32 hex digits: " + answer.upload);
        return answer;
    }

    lf::FileAnswer SendPiece(lf::FileConnection& connection, const std::string& upload, const std::string& source, std::int64_t number)
    {
        auto piece = BuildPiece(source, kPieceBytes, number);
        return connection.Piece(upload, number, piece.length, piece.digest, piece.frame);
    }

    void SendPieces(lf::FileConnection& connection, const std::string& upload, const std::string& source, std::int64_t from, std::int64_t to)
    {
        for (std::int64_t n = from; n < to; ++n)
        {
            const auto answer = SendPiece(connection, upload, source, n);
            CheckKind(Kind::Next, answer, "piece " + std::to_string(n));
            Check(answer.next == n + 1, "piece " + std::to_string(n) + ": next " + std::to_string(answer.next));
        }
    }

    void EndStored(lf::FileConnection& connection, const std::string& upload, const std::string& source)
    {
        const auto answer = connection.End(upload, HashOf(source, kPieceBytes));
        CheckKind(Kind::Stored, answer, "end");
        Check(answer.stored_ticks == kStoredTicks, "end: the value's time");
    }

    void CheckStored(const FileRig& rig, const std::string& source, const std::string& name = "app_20261125.dmp")
    {
        const auto stored = rig.script->StoredFiles();
        const auto found = stored.find(name);
        Check(found != stored.end(), "the file must be stored as " + name);
        Check(found->second == source, "the stored bytes must be the source's (" + std::to_string(found->second.size()) + " of " + std::to_string(source.size()) + ")");
    }

    // The upgrade names the subprotocol and the session; the hello is read; the probe goes first.
    void NativeFileConnectionOpenSendsTheUpgradeHelloAndProbe()
    {
        if (!WebSocketOrSkip("native_file_connection_open_sends_the_upgrade_hello_and_probe"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);

        const auto heads = rig.server.SideUpgradeHeads();
        Check(heads.size() == 1, "one file upgrade");
        Check(hsm::test::ws_detail::HeaderValue(heads[0], "sec-websocket-protocol") == "hsm.files.v1", "the subprotocol: " + heads[0]);
        Check(hsm::test::ws_detail::HeaderValue(heads[0], "x-hsm-session") == rig.SessionId(), "X-Hsm-Session names the live session");
        Check(hsm::test::ws_detail::HeaderValue(heads[0], "key") == "file-key", "the session's Key header");

        const auto& hello = connection.Hello();
        Check(hello.max_frame_bytes == kFrameBytes, "the hello's maxFrameBytes");
        Check(hello.heartbeat_ms == 30000, "the hello's heartbeat");
        Check(hello.max_file_bytes == kMaxFileBytes, "the hello's maxFileBytes");
        Check(hello.piece_bytes == rig.script->PieceBytes() && hello.piece_bytes == Script::PieceBytesFor(kFrameBytes), "the hello's pieceBytes");
        Check(Script::PieceBytesFor(4194304) == 4173824, "pieceBytes of the default frame, as the contract says");

        const auto probes = rig.script->Probes();
        Check(probes.size() == 1 && probes[0].connection == 1, "one probe on the first connection");
        const auto connections = rig.script->Connections();
        Check(connections.size() == 1 && connections[0].session_id == rig.SessionId(), "one file connection of the session");
        Check(connection.IsOpen(), "the connection stays open");
    }

    // begin, every piece, end: stored, and the server's bytes are the source's.
    void NativeFileConnectionWholeUploadIsStored()
    {
        if (!WebSocketOrSkip("native_file_connection_whole_upload_is_stored"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, begun.pieces);
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);

        const auto begins = rig.script->Begins();
        Check(begins.size() == 1, "one begin");
        Check(begins[0].path == "app/dumps" && begins[0].name == "app_20261125.dmp" && begins[0].size == static_cast<std::int64_t>(source.size()),
              "the begin's names: " + begins[0].raw);
        Check(begins[0].modified == "2026-11-25T10:00:00.1234567Z", "the begin's modified: " + begins[0].modified);
        Check(begins[0].has_source && begins[0].source == "vol:5b2e/file:00a1", "the begin's source");
        Check(begins[0].piece_bytes == kPieceBytes && begins[0].status == 200, "the begin's pieceBytes and answer");
        const auto pieces = rig.script->Pieces();
        Check(static_cast<std::int64_t>(pieces.size()) == begun.pieces, "every piece once");
        for (std::size_t n = 0; n < pieces.size(); ++n)
        {
            Check(pieces[n].number == static_cast<std::int64_t>(n) && pieces[n].status == 200, "piece " + std::to_string(n) + " stored in order");
            Check(pieces[n].upload == begun.upload, "piece " + std::to_string(n) + " names the upload");
            const std::uint32_t expected = n + 1 < pieces.size() ? static_cast<std::uint32_t>(kPieceBytes) : static_cast<std::uint32_t>(source.size() % kPieceBytes);
            Check(pieces[n].decompressed_length == expected, "piece " + std::to_string(n) + ": its decompressed length");
            Check(pieces[n].frame_bytes == lf::kPieceHeaderBytes + pieces[n].compressed_length, "piece " + std::to_string(n) + ": its compressed length");
        }
        const auto ends = rig.script->Ends();
        Check(ends.size() == 1 && ends[0].hash == HashOf(source, kPieceBytes) && ends[0].status == 200, "one end with the hash of the digests");
    }

    // A file of no bytes: no pieces, and the hash of nothing.
    void NativeFileConnectionEmptyFileIsStored()
    {
        if (!WebSocketOrSkip("native_file_connection_empty_file_is_stored"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source;
        const auto begun = Begin(connection, source);
        Check(begun.pieces == 0, "no pieces");
        Check(HashOf(source, kPieceBytes) == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "the hash of nothing");
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);
    }

    // The connection is cut after two pieces; on a new one begin answers next 2 and the upload goes on.
    void NativeFileConnectionContinuesAfterACut()
    {
        if (!WebSocketOrSkip("native_file_connection_continues_after_a_cut"))
            return;
        FileRig rig([](Script& script) {
            script.AnswerPieceNext(Answer::Default());
            script.AnswerPieceNext(Answer::Default());
            script.AnswerPieceNext(Answer::Cut());
        });
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, 2);
        CheckKind(Kind::Lost, SendPiece(connection, begun.upload, source, 2), "the piece whose connection was cut");
        Check(!connection.IsOpen(), "the connection is over");

        rig.Open(connection);
        const auto again = Begin(connection, source, 2);
        Check(again.upload == begun.upload, "the same upload goes on");
        SendPieces(connection, again.upload, source, 2, again.pieces);
        EndStored(connection, again.upload, source);
        CheckStored(rig, source);
        Check(rig.script->Connections().size() == 2, "two file connections");
    }

    // A piece whose CRC-32 does not match is refused with its number; sent again, it is stored.
    void NativeFileConnectionChecksumMismatchSendsThePieceAgain()
    {
        if (!WebSocketOrSkip("native_file_connection_checksum_mismatch_sends_the_piece_again"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, 1);
        rig.script->CorruptNextPieceCrcs(1);
        const auto refused = SendPiece(connection, begun.upload, source, 1);
        CheckKind(Kind::ChecksumMismatch, refused, "a piece with a damaged CRC");
        Check(refused.status == 409 && refused.next == 1, "409 with the server's next");
        SendPieces(connection, begun.upload, source, 1, begun.pieces);
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);
    }

    // A piece the server has already is answered with the server's next, and stored once.
    void NativeFileConnectionRepeatPieceAnswersTheServersNext()
    {
        if (!WebSocketOrSkip("native_file_connection_repeat_piece_answers_the_servers_next"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, 2);
        const auto repeat = SendPiece(connection, begun.upload, source, 0);
        CheckKind(Kind::Next, repeat, "a repeat");
        Check(repeat.status == 200 && repeat.next == 2, "200 with the server's next 2, got " + std::to_string(repeat.next));
        SendPieces(connection, begun.upload, source, 2, begun.pieces);
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);
    }

    // A piece ahead of the server's next is answered 409 "out of order" with the next to go on from.
    void NativeFileConnectionPieceAheadAnswersOutOfOrder()
    {
        if (!WebSocketOrSkip("native_file_connection_piece_ahead_answers_out_of_order"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, 1);
        const auto ahead = SendPiece(connection, begun.upload, source, 2);
        CheckKind(Kind::Next, ahead, "a piece ahead");
        Check(ahead.status == 409 && ahead.next == 1, "409 with the server's next 1, got " + std::to_string(ahead.next));
        SendPieces(connection, begun.upload, source, 1, begun.pieces);
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);
    }

    // A piece whose bytes are not one zstd frame of its length discards the upload; begin starts from 0.
    void NativeFileConnectionBadZstdFrameDiscards()
    {
        if (!WebSocketOrSkip("native_file_connection_bad_zstd_frame_discards"))
            return;
        const std::string source = Source();
        for (const bool garbage : { true, false })
        {
            const std::string what = garbage ? "bytes that are not zstd: " : "a frame of another content size: ";
            FileRig rig;
            lf::FileConnection connection(rig.Options());
            rig.Open(connection);
            const auto begun = Begin(connection, source);
            SendPieces(connection, begun.upload, source, 0, 1);

            BuiltPiece bad;
            if (garbage)
            {
                bad.frame.assign(lf::kPieceHeaderBytes + 1000, 0);
                for (std::size_t i = lf::kPieceHeaderBytes; i < bad.frame.size(); ++i)
                    bad.frame[i] = static_cast<std::uint8_t>(i * 7);
            }
            else
            {
                // A whole zstd frame of 1,000 bytes, sent as piece 1 of 65,536.
                bad = BuildPiece(source.substr(0, 1000), kPieceBytes, 0);
            }
            const auto good = BuildPiece(source, kPieceBytes, 1);
            const auto discarded = connection.Piece(begun.upload, 1, good.length, good.digest, bad.frame);
            CheckKind(Kind::Discarded, discarded, what + "the piece");
            Check(discarded.status == 400 && discarded.error.find("the upload is discarded") != std::string::npos, what + "400 discarded: " + discarded.error);
            CheckKind(Kind::NotHeld, SendPiece(connection, begun.upload, source, 1), what + "a piece after the discard");

            const auto again = Begin(connection, source, 0);
            Check(again.upload != begun.upload, what + "a new upload");
            SendPieces(connection, again.upload, source, 0, again.pieces);
            EndStored(connection, again.upload, source);
            CheckStored(rig, source);
        }
    }

    // The end check fails, on the hash or on a piece's digest: the part is removed, begin starts from 0.
    void NativeFileConnectionEndCheckFailureDiscards()
    {
        if (!WebSocketOrSkip("native_file_connection_end_check_failure_discards"))
            return;
        const std::string source = Source();
        for (const bool wrong_hash : { true, false })
        {
            const std::string what = wrong_hash ? "a wrong hash: " : "a piece with a wrong digest: ";
            FileRig rig;
            lf::FileConnection connection(rig.Options());
            rig.Open(connection);
            const auto begun = Begin(connection, source);
            std::string hash;
            if (wrong_hash)
            {
                SendPieces(connection, begun.upload, source, 0, begun.pieces);
                hash = std::string(64, '0');
            }
            else
            {
                std::vector<lf::Sha256Digest> digests;
                for (std::int64_t n = 0; n < begun.pieces; ++n)
                {
                    auto piece = BuildPiece(source, kPieceBytes, n);
                    if (n == 1)
                        piece.digest[0] ^= 0x01; // the digest of other bytes
                    digests.push_back(piece.digest);
                    CheckKind(Kind::Next, connection.Piece(begun.upload, n, piece.length, piece.digest, piece.frame), what + "piece " + std::to_string(n));
                }
                hash = HashOfDigests(digests); // the hash of what was sent: only the piece check can fail
            }
            const auto ended = connection.End(begun.upload, hash);
            CheckKind(Kind::Discarded, ended, what + "end");
            Check(ended.status == 400 && ended.error.find("the upload is discarded") != std::string::npos, what + "400 discarded: " + ended.error);
            Check(wrong_hash || ended.error.find("piece 1") != std::string::npos, what + "the error names the piece: " + ended.error);
            Check(rig.script->StoredFiles().empty(), what + "nothing stored");
            Check(rig.script->Parts() == 0, what + "the part is removed");

            const auto again = Begin(connection, source, 0);
            Check(again.upload != begun.upload, what + "a new upload");
        }
    }

    // end answered 202 "checking" twice, then stored; a begin during the check answers checking too.
    void NativeFileConnectionEndCheckingThenStored()
    {
        if (!WebSocketOrSkip("native_file_connection_end_checking_then_stored"))
            return;
        FileRig rig([](Script& script) { script.SetCheckingRounds(2); });
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, begun.pieces);
        for (int round = 0; round < 2; ++round)
        {
            const auto checking = connection.End(begun.upload, HashOf(source, kPieceBytes));
            CheckKind(Kind::Checking, checking, "end round " + std::to_string(round));
            Check(checking.status == 202 && checking.retry_after_ms == 1000, "202 with retryAfter 1 s");
        }
        EndStored(connection, begun.upload, source);
        CheckStored(rig, source);
        Check(rig.script->Ends().size() == 3, "three ends");

        // A begin of a stored file: stored, nothing to send.
        const auto stored = connection.Begin(Facts(source), kPieceBytes);
        CheckKind(Kind::Stored, stored, "a begin of the stored file");
        Check(stored.upload == begun.upload, "the stored file's upload");
    }

    // discard removes the part: a piece after it is not held; a second discard is not held either.
    void NativeFileConnectionDiscardThenPieceIsNotHeld()
    {
        if (!WebSocketOrSkip("native_file_connection_discard_then_piece_is_not_held"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, 1);
        const auto discarded = connection.Discard(begun.upload);
        CheckKind(Kind::DiscardDone, discarded, "discard");
        Check(discarded.status == 200, "discard 200");
        CheckKind(Kind::NotHeld, SendPiece(connection, begun.upload, source, 1), "a piece after the discard");
        CheckKind(Kind::NotHeld, connection.Discard(begun.upload), "a second discard");
        Check(rig.script->Parts() == 0, "the part is removed");
        const auto discards = rig.script->Discards();
        Check(discards.size() == 2 && discards[0].upload == begun.upload && discards[0].status == 200 && discards[1].status == 404, "the discards recorded");
        Begin(connection, source, 0);
    }

    // A server before the discard op answers it 400 "unknown op".
    void NativeFileConnectionDiscardOnAnOldServerIsUnknownOp()
    {
        if (!WebSocketOrSkip("native_file_connection_discard_on_an_old_server_is_unknown_op"))
            return;
        FileRig rig([](Script& script) { script.SetDiscardUnknown(true); });
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        const auto answer = connection.Discard(begun.upload);
        CheckKind(Kind::UnknownOp, answer, "discard on an old server");
        Check(answer.status == 400, "400");
        Check(connection.IsOpen(), "the connection stays open");
    }

    // Every answer a begin can get reaches its kind, scripted or by the script's own checks.
    void NativeFileConnectionEveryBeginStatusReachesItsKind()
    {
        if (!WebSocketOrSkip("native_file_connection_every_begin_status_reaches_its_kind"))
            return;
        const std::string upload = "f11e00000000000000000000000000aa";
        struct Case
        {
            const char* what;
            Answer answer;
            Kind kind;
            std::int64_t retry_after_ms;
        };
        const std::vector<Case> cases = {
            { "200 stored", Answer::WithStatus(200, -1, "{\"upload\":\"" + upload + "\",\"state\":\"stored\"}"), Kind::Stored, -1 },
            { "202 checking", Answer::WithStatus(202, 3, "{\"upload\":\"" + upload + "\",\"state\":\"checking\"}"), Kind::Checking, 3000 },
            { "503", Answer::WithStatus(503, 5, {}, "no room"), Kind::RetryLater, 5000 },
            { "409 busy", Answer::WithStatus(409, 1, {}, "busy"), Kind::RetryLater, 1000 },
            { "413", Answer::WithStatus(413, -1, {}, "too large"), Kind::Final, -1 },
            { "422", Answer::WithStatus(422, -1, {}, "no File sensor"), Kind::Final, -1 },
            { "400", Answer::WithStatus(400, -1, {}, "malformed"), Kind::Final, -1 },
        };
        FileRig rig([&](Script& script) {
            for (const auto& scripted : cases)
                script.AnswerBeginNext(scripted.answer);
        });
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        for (const auto& scripted : cases)
        {
            const auto answer = connection.Begin(Facts(source), kPieceBytes);
            CheckKind(scripted.kind, answer, scripted.what);
            Check(answer.status == scripted.answer.status, std::string(scripted.what) + ": the status");
            Check(answer.retry_after_ms == scripted.retry_after_ms, std::string(scripted.what) + ": retryAfter " + std::to_string(answer.retry_after_ms));
        }
        Check(rig.script->Parts() == 0, "a scripted answer changes nothing");

        // The script's own refusals: over maxFileBytes, a piece below 64 KiB.
        auto large = Facts(source);
        large.size = kMaxFileBytes + 1;
        const auto too_large = connection.Begin(large, kPieceBytes);
        CheckKind(Kind::Final, too_large, "a file over maxFileBytes");
        Check(too_large.status == 413, "413");
        const auto small = connection.Begin(Facts(source), 4096);
        CheckKind(Kind::Final, small, "pieceBytes below 64 KiB");
        Check(small.status == 400, "400");

        // A check that runs when the begin comes, then the stored file.
        rig.script->SetCheckingRounds(1);
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, begun.pieces);
        CheckKind(Kind::Checking, connection.End(begun.upload, HashOf(source, kPieceBytes)), "end");
        rig.script->SetCheckingRounds(1);
        const auto checking = connection.Begin(Facts(source), kPieceBytes);
        CheckKind(Kind::Checking, checking, "a begin while the check runs");
        Check(checking.retry_after_ms == 1000, "retryAfter 1 s");
        CheckKind(Kind::Stored, connection.Begin(Facts(source), kPieceBytes), "a begin once the check is done");
        CheckStored(rig, source);
    }

    // An answer that never comes: the request ends as lost and the connection is closed; a new one works.
    void NativeFileConnectionSilentAnswerIsLost()
    {
        if (!WebSocketOrSkip("native_file_connection_silent_answer_is_lost"))
            return;
        FileRig rig([](Script& script) {
            script.AnswerBeginNext(Answer::NoAnswer());
            script.AnswerEndNext(Answer::NoAnswer());
        });
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string source = Source();
        const auto lost = connection.Begin(Facts(source), kPieceBytes);
        CheckKind(Kind::Lost, lost, "a begin without an answer");
        Check(lost.error.find("no ack") != std::string::npos, "the error says why: " + lost.error);
        Check(!connection.IsOpen(), "the connection is closed");

        rig.Open(connection);
        const auto begun = Begin(connection, source);
        SendPieces(connection, begun.upload, source, 0, begun.pieces);
        CheckKind(Kind::Lost, connection.End(begun.upload, HashOf(source, kPieceBytes)), "an end without an answer");
        rig.Open(connection);
        const auto again = Begin(connection, source, begun.pieces);
        EndStored(connection, again.upload, source);
        CheckStored(rig, source);
    }

    // Cancel from another thread ends a request that waits for its ack; nothing else could end it.
    void NativeFileConnectionCancelEndsAWait()
    {
        if (!WebSocketOrSkip("native_file_connection_cancel_ends_a_wait"))
            return;
        FileRig rig([](Script& script) { script.AnswerPieceNext(Answer::NoAnswer()); });
        auto options = rig.Options();
        options.ack_abandoned_for_tests = nullptr; // only the cancel (or ten minutes) can end the wait
        lf::FileConnection connection(options);
        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);

        lf::FileAnswer answer;
        std::thread owner([&] { answer = SendPiece(connection, begun.upload, source, 0); });
        try
        {
            WaitOrFail(
                [&] {
                    const auto pieces = rig.script->Pieces();
                    return !pieces.empty() && rig.script->Unanswered(pieces.back().id);
                },
                "the piece left unanswered");
        }
        catch (...)
        {
            connection.Cancel();
            owner.join();
            throw;
        }
        connection.Cancel();
        owner.join();
        CheckKind(Kind::Cancelled, answer, "the piece waiting for its ack");
        CheckKind(Kind::Cancelled, connection.Begin(Facts(source), kPieceBytes), "a request after the cancel");

        connection.ResetCancel();
        rig.Open(connection);
        const auto again = Begin(connection, source, 0);
        Check(again.upload == begun.upload, "the same upload");
        SendPieces(connection, again.upload, source, 0, again.pieces);
        EndStored(connection, again.upload, source);
        CheckStored(rig, source);
    }

    // A second file connection of the session replaces the first, which gives its upload back.
    void NativeFileConnectionANewConnectionReplacesTheOld()
    {
        if (!WebSocketOrSkip("native_file_connection_a_new_connection_replaces_the_old"))
            return;
        FileRig rig;
        const std::string source = Source();
        lf::FileConnection first(rig.Options());
        rig.Open(first);
        const auto begun = Begin(first, source);
        SendPieces(first, begun.upload, source, 0, 1);

        lf::FileConnection second(rig.Options());
        rig.Open(second);
        const auto again = Begin(second, source, 1);
        Check(again.upload == begun.upload, "the same upload, not busy");

        WaitOrFail([&] { return !first.Service(); }, "the first connection to end");
        Check(first.LastEnd().server_close == 1000, "the first is closed with 1000, got " + std::to_string(first.LastEnd().server_close));
        Check(first.LastEnd().why.find("replaced by a new file connection of its session") != std::string::npos, "the reason: " + first.LastEnd().why);

        SendPieces(second, again.upload, source, 1, again.pieces);
        EndStored(second, again.upload, source);
        CheckStored(rig, source);
    }

    // When the session ends its file connection is closed with 1000 "its session ended".
    void NativeFileConnectionSessionEndClosesIt()
    {
        if (!WebSocketOrSkip("native_file_connection_session_end_closes_it"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        rig.session->Stop(0);
        WaitOrFail([&] { return !connection.Service(); }, "the file connection to end");
        Check(connection.LastEnd().server_close == 1000, "closed with 1000, got " + std::to_string(connection.LastEnd().server_close));
        Check(connection.LastEnd().why.find("its session ended") != std::string::npos, "the reason: " + connection.LastEnd().why);
        const auto opened = connection.Open(rig.SessionId());
        Check(!opened.ok && opened.status == 409, "no live session: the upgrade is refused with 409, got " + std::to_string(opened.status));
    }

    // A hello whose pieceBytes cannot fit its frame is refused, and the connection closed with 1002.
    void NativeFileConnectionUnusableHelloIsRefused()
    {
        if (!WebSocketOrSkip("native_file_connection_unusable_hello_is_refused"))
            return;
        FileRig rig([](Script& script) {
            script.SetHelloOverride("{\"op\":\"hello\",\"body\":{\"maxFrameBytes\":262144,\"heartbeatSeconds\":30,\"fileLimits\":{\"maxFileBytes\":1048576,"
                                    "\"pieceBytes\":262144}}}");
        });
        lf::FileConnection connection(rig.Options());
        const auto opened = connection.Open(rig.SessionId());
        Check(!opened.ok && opened.protocol_error, "the hello must be refused");
        Check(opened.kind == "hello:protocol:limits", "the kind: " + opened.kind);
        Check(rig.script->Probes().empty(), "no probe after a refused hello");
    }

    // What the server cannot answer closes the connection: a piece over 64 KiB without an upload (1002),
    // a frame over maxFrameBytes (1009).
    void NativeFileConnectionProtocolErrorsCloseTheConnection()
    {
        if (!WebSocketOrSkip("native_file_connection_protocol_errors_close_the_connection"))
            return;
        FileRig rig;
        lf::FileConnection connection(rig.Options());
        rig.Open(connection);
        const std::string upload = "f11e00000000000000000000000000bb";

        lf::Sha256Digest digest{};
        std::vector<std::uint8_t> large(lf::kPieceHeaderBytes + 70000, 0x11);
        CheckKind(Kind::Lost, connection.Piece(upload, 0, 70000, digest, large), "a piece over 64 KiB without an upload");
        Check(connection.LastEnd().server_close == 1002, "closed with 1002, got " + std::to_string(connection.LastEnd().server_close));

        rig.Open(connection);
        const std::string source = Source();
        const auto begun = Begin(connection, source);
        std::vector<std::uint8_t> over(static_cast<std::size_t>(kFrameBytes) + 1, 0x22);
        CheckKind(Kind::Lost, connection.Piece(begun.upload, 0, static_cast<std::uint32_t>(kPieceBytes), digest, over), "a frame over maxFrameBytes");
        Check(connection.LastEnd().server_close == 1009, "closed with 1009, got " + std::to_string(connection.LastEnd().server_close));
    }
} // namespace
#endif

const std::map<std::string, std::function<void(const std::string&)>>& FileConnectionTests()
{
    static const std::map<std::string, std::function<void(const std::string&)>> tests = {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        { "native_file_connection_open_sends_the_upgrade_hello_and_probe", [](const std::string&) { NativeFileConnectionOpenSendsTheUpgradeHelloAndProbe(); } },
        { "native_file_connection_whole_upload_is_stored", [](const std::string&) { NativeFileConnectionWholeUploadIsStored(); } },
        { "native_file_connection_empty_file_is_stored", [](const std::string&) { NativeFileConnectionEmptyFileIsStored(); } },
        { "native_file_connection_continues_after_a_cut", [](const std::string&) { NativeFileConnectionContinuesAfterACut(); } },
        { "native_file_connection_checksum_mismatch_sends_the_piece_again", [](const std::string&) { NativeFileConnectionChecksumMismatchSendsThePieceAgain(); } },
        { "native_file_connection_repeat_piece_answers_the_servers_next", [](const std::string&) { NativeFileConnectionRepeatPieceAnswersTheServersNext(); } },
        { "native_file_connection_piece_ahead_answers_out_of_order", [](const std::string&) { NativeFileConnectionPieceAheadAnswersOutOfOrder(); } },
        { "native_file_connection_bad_zstd_frame_discards", [](const std::string&) { NativeFileConnectionBadZstdFrameDiscards(); } },
        { "native_file_connection_end_check_failure_discards", [](const std::string&) { NativeFileConnectionEndCheckFailureDiscards(); } },
        { "native_file_connection_end_checking_then_stored", [](const std::string&) { NativeFileConnectionEndCheckingThenStored(); } },
        { "native_file_connection_discard_then_piece_is_not_held", [](const std::string&) { NativeFileConnectionDiscardThenPieceIsNotHeld(); } },
        { "native_file_connection_discard_on_an_old_server_is_unknown_op", [](const std::string&) { NativeFileConnectionDiscardOnAnOldServerIsUnknownOp(); } },
        { "native_file_connection_every_begin_status_reaches_its_kind", [](const std::string&) { NativeFileConnectionEveryBeginStatusReachesItsKind(); } },
        { "native_file_connection_silent_answer_is_lost", [](const std::string&) { NativeFileConnectionSilentAnswerIsLost(); } },
        { "native_file_connection_cancel_ends_a_wait", [](const std::string&) { NativeFileConnectionCancelEndsAWait(); } },
        { "native_file_connection_a_new_connection_replaces_the_old", [](const std::string&) { NativeFileConnectionANewConnectionReplacesTheOld(); } },
        { "native_file_connection_session_end_closes_it", [](const std::string&) { NativeFileConnectionSessionEndClosesIt(); } },
        { "native_file_connection_unusable_hello_is_refused", [](const std::string&) { NativeFileConnectionUnusableHelloIsRefused(); } },
        { "native_file_connection_protocol_errors_close_the_connection", [](const std::string&) { NativeFileConnectionProtocolErrorsCloseTheConnection(); } },
#endif
    };
    return tests;
}
