#pragma once

// The server side of the file connection (#1887; wire contract "File connection", #1884), for
// WebSocketTestServer::EnableFileScript. Test-only; needs zstd, zlib and the collector's SHA-256, so it is
// compiled in the HSM_COLLECTOR_LARGE_FILES build only.
//
// What a server does, with real checks: the hello with the pieceBytes the wire defines, the probe, begin,
// the piece (its 88-byte header, the upload it names, its number and lengths, the CRC-32, the zstd frame,
// the order), end (every piece's SHA-256 against its digest, then the hash of the digests) and discard.
// Parts live in the script, keyed by the file's names, so they outlast a connection and Restart. Every
// request is recorded; any answer can be replaced by a scripted one (one per call, in order, per op).
// Thread-safe: the connection threads of the server call it at once.

#include "ws_test_server.hpp"

#include "../src/large_files/hsm_sha256.hpp"

#include <nlohmann/json.hpp>
#include <zlib.h>
#include <zstd.h>

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace hsm::test
{
    // How the script answers one request instead of what the contract says.
    struct FileScriptAnswer
    {
        enum class Kind
        {
            Default, // what the contract says
            Status,  // an ack with `status`, `retry_after` (when >= 0), `body` (JSON text, when not empty) and `error` (when not empty); nothing changes
            Silent,  // no answer; nothing changes
            Close,   // the connection closed with `code` and `reason`; nothing changes
            Drop,    // the TCP connection cut without a close frame; nothing changes
            // A 400 "...: the upload is discarded" that removes the part the connection holds, as the real
            // server does (a plain 400 when it holds none).
            DiscardPart,
            // The request is applied as the contract says (a piece stored, an end checked and stored), then the
            // TCP connection is cut before the answer goes out: an answer lost on the way.
            DropAfterStore,
        };
        Kind kind = Kind::Default;
        int status = 0;
        int retry_after = -1;
        std::string body;
        std::string error;
        int code = 0;
        std::string reason;

        static FileScriptAnswer Default() { return {}; }
        static FileScriptAnswer WithStatus(int status, int retry_after = -1, std::string body = {}, std::string error = {})
        {
            FileScriptAnswer answer;
            answer.kind = Kind::Status;
            answer.status = status;
            answer.retry_after = retry_after;
            answer.body = std::move(body);
            answer.error = std::move(error);
            return answer;
        }
        static FileScriptAnswer NoAnswer()
        {
            FileScriptAnswer answer;
            answer.kind = Kind::Silent;
            return answer;
        }
        static FileScriptAnswer CloseWith(int code, std::string reason = {})
        {
            FileScriptAnswer answer;
            answer.kind = Kind::Close;
            answer.code = code;
            answer.reason = std::move(reason);
            return answer;
        }
        static FileScriptAnswer Cut()
        {
            FileScriptAnswer answer;
            answer.kind = Kind::Drop;
            return answer;
        }
        static FileScriptAnswer Discarded()
        {
            FileScriptAnswer answer;
            answer.kind = Kind::DiscardPart;
            answer.status = 400;
            return answer;
        }
        static FileScriptAnswer CutAfterStore()
        {
            FileScriptAnswer answer;
            answer.kind = Kind::DropAfterStore;
            return answer;
        }
    };

    class FileServerScript : public FileSideHandler
    {
    public:
        static constexpr std::size_t kHeaderBytes = 88;
        static constexpr std::size_t kSmallFrameBytes = 64 * 1024;
        static constexpr const char* kStoredTime = "2026-11-25T12:00:00.0000000Z";

        // The answer status recorded for a request left without one (silent, a close or a cut).
        static constexpr int kNoAnswer = 0;

        struct BeginRecord
        {
            int connection = 0;
            std::int64_t id = 0;
            std::string path;
            std::string name;
            std::int64_t size = -1;
            std::string modified;
            std::string source;
            bool has_source = false;
            std::int64_t piece_bytes = -1;
            std::string raw;
            int status = kNoAnswer;
            bool path_ok = false;
            bool name_ok = false;
            bool modified_ok = false;
        };
        struct PieceRecord
        {
            int connection = 0;
            std::int64_t id = 0;
            std::string upload;
            std::int64_t number = -1;
            std::uint32_t decompressed_length = 0;
            std::uint32_t compressed_length = 0;
            std::size_t frame_bytes = 0;
            int status = kNoAnswer;
        };
        struct EndRecord
        {
            int connection = 0;
            std::int64_t id = 0;
            std::string upload;
            std::string hash;
            std::string raw;
            int status = kNoAnswer;
        };
        struct DiscardRecord
        {
            int connection = 0;
            std::int64_t id = 0;
            std::string upload;
            std::string raw;
            int status = kNoAnswer;
        };
        struct ProbeRecord
        {
            int connection = 0;
            std::int64_t id = 0;
        };
        struct ConnectionRecord
        {
            int connection = 0;
            std::string session_id;
            bool released = false;
        };

        FileServerScript() { SetHello(4 * 1024 * 1024, 30, 68719476736LL); }

        // The largest multiple of 4 KiB whose zstd bound fits a frame with the header.
        static std::int64_t PieceBytesFor(std::int64_t max_frame_bytes)
        {
            std::int64_t piece = (max_frame_bytes / 4096) * 4096;
            while (piece > 0 && static_cast<std::int64_t>(kHeaderBytes + ZSTD_compressBound(static_cast<std::size_t>(piece))) > max_frame_bytes)
                piece -= 4096;
            return piece;
        }

        // --- The script, set by the test. ---

        // What the hello of the connections accepted from now on states; pieceBytes follows from the frame.
        void SetHello(std::int64_t max_frame_bytes, int heartbeat_seconds, std::int64_t max_file_bytes)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            max_frame_bytes_ = max_frame_bytes;
            heartbeat_seconds_ = heartbeat_seconds;
            max_file_bytes_ = max_file_bytes;
            piece_bytes_ = PieceBytesFor(max_frame_bytes);
        }
        // The hello is sent as this text instead, when not empty (a broken server).
        void SetHelloOverride(std::string text)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            hello_override_ = std::move(text);
        }
        // A hello whose pieceBytes is exactly `piece_bytes`: maxFrameBytes is 88 + ZSTD_compressBound(piece_bytes).
        void SetLimits(std::int64_t piece_bytes, std::int64_t max_file_bytes)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            piece_bytes_ = piece_bytes;
            max_frame_bytes_ = static_cast<std::int64_t>(kHeaderBytes + ZSTD_compressBound(static_cast<std::size_t>(piece_bytes)));
            max_file_bytes_ = max_file_bytes;
        }
        std::int64_t PieceBytes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return piece_bytes_;
        }
        std::int64_t MaxFrameBytes() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return max_frame_bytes_;
        }

        void AnswerBeginNext(FileScriptAnswer answer) { Push(begin_answers_, std::move(answer)); }
        void AnswerPieceNext(FileScriptAnswer answer) { Push(piece_answers_, std::move(answer)); }
        void AnswerEndNext(FileScriptAnswer answer) { Push(end_answers_, std::move(answer)); }

        // The next `count` ends that would store a file answer 202 "checking" with retryAfter 1 instead.
        void SetCheckingRounds(int count)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            checking_rounds_ = count;
        }
        // The next end check finds the pieces from `from` on missing (a store that lost them): it answers 409
        // {"next":from,"reason":"pieces are missing"} and the part keeps only its first `from` pieces.
        void SetCheckMissingFrom(std::int64_t from)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            check_missing_from_ = from;
        }
        // discard is answered 400 "unknown op", as a server before the op does.
        void SetDiscardUnknown(bool unknown)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            discard_unknown_ = unknown;
        }
        // The CRC-32 field of the next `count` pieces is changed as they arrive (a frame damaged on the way).
        void CorruptNextPieceCrcs(int count)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            corrupt_crcs_ = count;
        }

        // A part the server holds already, unheld: its first `pieces_already` pieces are taken from
        // `content`. Returns its upload id.
        std::string Hold(const std::string& path, const std::string& name, std::int64_t size, const std::string& modified, const std::string& source,
                         std::int64_t piece_bytes, std::int64_t pieces_already, const std::string& content)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            const std::string key = KeyOf(path, name, size, modified, source);
            Part part = NewPart(name, size, piece_bytes);
            Preload(part, content, pieces_already);
            const std::string upload = part.upload;
            parts_[key] = std::move(part);
            return upload;
        }

        // When a part of a file named `name` is made (its first begin, or a begin after it was removed), the
        // server already has its first `pieces` pieces, taken from `content`.
        void HoldNamed(const std::string& name, std::string content, std::int64_t pieces)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            held_named_[name] = std::make_pair(std::move(content), pieces);
        }

        // Called with the file's name (empty when no part has the frame's upload) and the number of every piece
        // frame once its answer is decided, before it goes out. Called under the script's lock: it must not call
        // the script.
        void SetAfterPiece(std::function<void(const std::string& name, std::int64_t number)> hook)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            after_piece_ = std::move(hook);
        }

        // A restart of the server: no connection holds anything; the parts and stored files stay.
        void Restart()
        {
            std::lock_guard<std::mutex> guard(mutex_);
            held_.clear();
            last_end_.clear();
            checks_.clear();
            for (auto& part : parts_)
                part.second.holder = 0;
        }

        // --- What happened. ---

        std::vector<BeginRecord> Begins() const { return Snapshot(begins_); }
        std::vector<PieceRecord> Pieces() const { return Snapshot(pieces_); }
        std::vector<EndRecord> Ends() const { return Snapshot(ends_); }
        std::vector<DiscardRecord> Discards() const { return Snapshot(discards_); }
        std::vector<ProbeRecord> Probes() const { return Snapshot(probes_); }
        std::vector<ConnectionRecord> Connections() const { return Snapshot(connections_); }
        int Pings() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return pings_;
        }
        // The reassembled bytes of every file stored, by its name.
        std::map<std::string, std::string> StoredFiles() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return stored_files_;
        }
        // Stored parts of files of this name (a file stored again under other facts counts again).
        int StoredCount(const std::string& name) const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            int count = 0;
            for (const auto& part : parts_)
                count += part.second.stored && part.second.name == name ? 1 : 0;
            return count;
        }
        // Parts on the server, stored or not.
        std::size_t Parts() const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return parts_.size();
        }
        // True once the script decided to leave request `id` without an answer (for the client's
        // ack_abandoned_for_tests).
        bool Unanswered(std::int64_t id) const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return unanswered_.count(id) != 0;
        }

        // --- FileSideHandler. ---

        std::string HelloText() override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (!hello_override_.empty())
                return hello_override_;
            return "{\"op\":\"hello\",\"body\":{\"maxFrameBytes\":" + std::to_string(max_frame_bytes_) + ",\"heartbeatSeconds\":" +
                   std::to_string(heartbeat_seconds_) + ",\"fileLimits\":{\"maxFileBytes\":" + std::to_string(max_file_bytes_) +
                   ",\"pieceBytes\":" + std::to_string(piece_bytes_) + "}}}";
        }

        void Opened(int connection, const std::string& session_id) override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            connections_.push_back({ connection, session_id, false });
        }

        void Released(int connection) override
        {
            std::lock_guard<std::mutex> guard(mutex_);
            GiveBack(connection);
            last_end_.erase(connection);
            checks_.erase(connection);
            for (auto& record : connections_)
                if (record.connection == connection)
                    record.released = true;
        }

        FileSideAction OnText(int connection, const std::string& text) override
        {
            try
            {
                const auto json = nlohmann::json::parse(text, nullptr, /*allow_exceptions=*/false);
                if (!json.is_object())
                    return FileSideAction::CloseWith(1007, "a text frame that is not a JSON object");
                const auto op_found = json.find("op");
                const std::string op = op_found != json.end() && op_found->is_string() ? op_found->get<std::string>() : std::string();
                if (op == "ping")
                {
                    std::lock_guard<std::mutex> guard(mutex_);
                    ++pings_;
                    return FileSideAction::ReplyWith("{\"op\":\"pong\"}");
                }
                if (op == "pong")
                    return FileSideAction::Nothing();
                const auto id_found = json.find("id");
                if (id_found == json.end() || !id_found->is_number_integer())
                    return FileSideAction::CloseWith(1002, "a request without id");
                const std::int64_t id = id_found->get<std::int64_t>();
                const auto body_found = json.find("body");
                const nlohmann::json body = body_found != json.end() && body_found->is_object() ? *body_found : nlohmann::json::object();

                std::lock_guard<std::mutex> guard(mutex_);
                if (op == "begin")
                    return OnBegin(connection, id, body, text);
                if (op == "end")
                    return OnEnd(connection, id, body, text);
                if (op == "discard")
                    return OnDiscard(connection, id, body, text);
                return FileSideAction::ReplyWith(Ack(id, 400, {}, "unknown op"));
            }
            catch (...)
            {
                return FileSideAction::CloseWith(1011, "the script failed");
            }
        }

        FileSideAction OnBinary(int connection, const std::string& frame) override
        {
            try
            {
                std::lock_guard<std::mutex> guard(mutex_);
                return OnFrame(connection, frame);
            }
            catch (...)
            {
                return FileSideAction::CloseWith(1011, "the script failed");
            }
        }

    private:
        struct Part
        {
            std::string upload;
            std::string name;
            std::int64_t size = 0;
            std::int64_t piece_bytes = 0;
            std::int64_t pieces = 0;
            std::int64_t next = 0;
            std::vector<std::string> data; // the decompressed pieces 0..next-1
            std::vector<collector::large_files::Sha256Digest> digests;
            int holder = 0;
            bool checking = false;
            bool stored = false;
            std::string end_hash; // lower-case hex, of the end that began the check
        };
        struct EndAnswer
        {
            std::string upload;
            int status = 0;
            std::string body;
            std::string error;
        };

        template <class T>
        std::vector<T> Snapshot(const std::vector<T>& records) const
        {
            std::lock_guard<std::mutex> guard(mutex_);
            return records;
        }

        void Push(std::deque<FileScriptAnswer>& queue, FileScriptAnswer answer)
        {
            std::lock_guard<std::mutex> guard(mutex_);
            queue.push_back(std::move(answer));
        }

        static FileScriptAnswer Next(std::deque<FileScriptAnswer>& queue)
        {
            if (queue.empty())
                return FileScriptAnswer::Default();
            FileScriptAnswer answer = std::move(queue.front());
            queue.pop_front();
            return answer;
        }

        static std::string KeyOf(const std::string& path, const std::string& name, std::int64_t size, const std::string& modified, const std::string& source)
        {
            // Unit separators keep the fields apart whatever they hold.
            return path + '\x1f' + name + '\x1f' + std::to_string(size) + '\x1f' + modified + '\x1f' + source;
        }

        static std::string Ack(std::int64_t id, int status, const std::string& body_json, const std::string& error, int retry_after = -1)
        {
            std::string text = "{\"ack\":" + std::to_string(id) + ",\"status\":" + std::to_string(status);
            if (!error.empty())
                text += ",\"error\":" + nlohmann::json(error).dump();
            if (retry_after >= 0)
                text += ",\"retryAfter\":" + std::to_string(retry_after);
            if (!body_json.empty())
                text += ",\"body\":" + body_json;
            return text + "}";
        }

        static std::int64_t ExpectedLength(const Part& part, std::int64_t number)
        {
            return number < part.pieces - 1 ? part.piece_bytes : part.size - number * part.piece_bytes;
        }

        Part NewPart(const std::string& name, std::int64_t size, std::int64_t piece_bytes)
        {
            Part part;
            char serial[17];
            std::snprintf(serial, sizeof serial, "%016llx", static_cast<unsigned long long>(++uploads_));
            part.upload = std::string("f11e0000000000000000000000000000").substr(0, 16) + serial;
            part.name = name;
            part.size = size;
            part.piece_bytes = piece_bytes;
            part.pieces = size == 0 ? 0 : (size + piece_bytes - 1) / piece_bytes;
            return part;
        }

        // The first `pieces` pieces of `part`, taken from `content`.
        static void Preload(Part& part, const std::string& content, std::int64_t pieces)
        {
            for (std::int64_t n = part.next; n < pieces && n < part.pieces; ++n)
            {
                const std::size_t offset = static_cast<std::size_t>(n * part.piece_bytes);
                if (offset > content.size())
                    break;
                const std::string bytes = content.substr(offset, static_cast<std::size_t>(ExpectedLength(part, n)));
                collector::large_files::Sha256Digest digest{};
                collector::large_files::Sha256Of(bytes.data(), bytes.size(), digest);
                part.data.push_back(bytes);
                part.digests.push_back(digest);
                part.next = n + 1;
            }
        }

        // The part the connection holds; null when it holds none.
        Part* HeldBy(int connection)
        {
            const auto held = held_.find(connection);
            if (held == held_.end())
                return nullptr;
            const auto part = parts_.find(held->second);
            return part == parts_.end() ? nullptr : &part->second;
        }

        void GiveBack(int connection)
        {
            const auto held = held_.find(connection);
            if (held == held_.end())
                return;
            const auto part = parts_.find(held->second);
            if (part != parts_.end() && part->second.holder == connection)
                part->second.holder = 0;
            held_.erase(held);
        }

        // The part is removed (a discard, a failed check); the connection holds nothing after it.
        void Remove(int connection)
        {
            const auto held = held_.find(connection);
            if (held == held_.end())
                return;
            parts_.erase(held->second);
            held_.erase(held);
        }

        // A scripted answer that is not Default, applied; `status` is what was answered.
        FileSideAction Scripted(const FileScriptAnswer& answer, std::int64_t id, int& status, int connection)
        {
            switch (answer.kind)
            {
            case FileScriptAnswer::Kind::Default:
            case FileScriptAnswer::Kind::DropAfterStore:
                break;
            case FileScriptAnswer::Kind::DiscardPart:
                status = 400;
                if (HeldBy(connection) == nullptr)
                    return FileSideAction::ReplyWith(Ack(id, 400, {}, "scripted"));
                Remove(connection);
                return FileSideAction::ReplyWith(Ack(id, 400, {}, "scripted: the upload is discarded"));
            case FileScriptAnswer::Kind::Status:
                status = answer.status;
                return FileSideAction::ReplyWith(Ack(id, answer.status, answer.body, answer.error, answer.retry_after));
            case FileScriptAnswer::Kind::Silent:
                unanswered_.insert(id);
                return FileSideAction::Nothing();
            case FileScriptAnswer::Kind::Close:
                return FileSideAction::CloseWith(answer.code, answer.reason);
            case FileScriptAnswer::Kind::Drop:
                return FileSideAction::Cut();
            }
            return FileSideAction::Nothing();
        }

        // The next scripted answer of `queue`, or the real one (`real`); DropAfterStore runs the real one and cuts.
        template <class Real>
        FileSideAction Apply(std::deque<FileScriptAnswer>& queue, std::int64_t id, int& status, int connection, Real real)
        {
            const FileScriptAnswer scripted = Next(queue);
            if (scripted.kind == FileScriptAnswer::Kind::Default)
                return real();
            if (scripted.kind == FileScriptAnswer::Kind::DropAfterStore)
            {
                real();
                status = kNoAnswer;
                return FileSideAction::Cut();
            }
            return Scripted(scripted, id, status, connection);
        }

        static std::string StringField(const nlohmann::json& body, const char* name, bool& present)
        {
            const auto found = body.find(name);
            present = found != body.end() && found->is_string();
            return present ? found->get<std::string>() : std::string();
        }

        static std::int64_t IntField(const nlohmann::json& body, const char* name)
        {
            const auto found = body.find(name);
            return found != body.end() && found->is_number_integer() ? found->get<std::int64_t>() : -1;
        }

        static bool IsHex(const std::string& text, std::size_t size)
        {
            if (text.size() != size)
                return false;
            for (const char c : text)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        static std::string Lower(std::string text)
        {
            for (auto& c : text)
                if (c >= 'A' && c <= 'F')
                    c = static_cast<char>(c - 'A' + 'a');
            return text;
        }

        FileSideAction OnBegin(int connection, std::int64_t id, const nlohmann::json& body, const std::string& raw)
        {
            BeginRecord record;
            record.connection = connection;
            record.id = id;
            record.raw = raw;
            record.path = StringField(body, "path", record.path_ok);
            record.name = StringField(body, "name", record.name_ok);
            record.modified = StringField(body, "modified", record.modified_ok);
            record.source = StringField(body, "source", record.has_source);
            record.size = IntField(body, "size");
            record.piece_bytes = IntField(body, "pieceBytes");
            begins_.push_back(record);
            int& status = begins_.back().status;
            return Apply(begin_answers_, id, status, connection, [&] { return BeginReal(connection, id, record, status); });
        }

        FileSideAction BeginReal(int connection, std::int64_t id, const BeginRecord& record, int& status)
        {
            const auto answer = [&](int code, const std::string& body_json, const std::string& error, int retry_after = -1) {
                status = code;
                return FileSideAction::ReplyWith(Ack(id, code, body_json, error, retry_after));
            };
            const bool source_ok = !record.has_source || (!record.source.empty() && record.source.size() <= 256);
            if (!record.path_ok || record.path.empty() || !record.name_ok || record.name.empty() || record.name.size() > 1024 || !record.modified_ok ||
                record.modified.empty() ||
                record.size < 0 || !source_ok || record.piece_bytes < 65536 || record.piece_bytes > piece_bytes_)
                return answer(400, {}, "a malformed begin");
            if (record.size > max_file_bytes_)
                return answer(413, {}, "larger than maxFileBytes");

            // A connection holds one upload: the old one is given back first.
            GiveBack(connection);
            last_end_.erase(connection);
            checks_.erase(connection);

            const std::string key = KeyOf(record.path, record.name, record.size, record.modified, record.source);
            auto found = parts_.find(key);
            if (found != parts_.end())
            {
                Part& part = found->second;
                if (part.stored)
                    return answer(200, "{\"upload\":\"" + part.upload + "\",\"state\":\"stored\"}", {});
                if (part.holder != 0 && part.holder != connection)
                    return answer(409, {}, "busy: another connection holds this file", 1);
                if (part.checking && checking_rounds_ > 0)
                {
                    --checking_rounds_;
                    return answer(202, "{\"upload\":\"" + part.upload + "\",\"state\":\"checking\"}", {}, 1);
                }
                if (part.checking)
                {
                    // The check is done by now: stored, pieces missing (the part waits for them), or the part
                    // is removed and the file begins anew.
                    std::string error;
                    const CheckOutcome checked = FinishCheck(part, error);
                    if (checked == CheckOutcome::Stored)
                        return answer(200, StoredBody(part.upload, false), {});
                    if (checked == CheckOutcome::Failed)
                    {
                        parts_.erase(found);
                        found = parts_.end();
                    }
                }
                else if (part.piece_bytes != record.piece_bytes)
                {
                    parts_.erase(found);
                    found = parts_.end();
                }
            }
            if (found == parts_.end())
            {
                found = parts_.emplace(key, NewPart(record.name, record.size, record.piece_bytes)).first;
                const auto held = held_named_.find(record.name);
                if (held != held_named_.end())
                    Preload(found->second, held->second.first, held->second.second);
            }
            Part& part = found->second;
            part.holder = connection;
            held_[connection] = key;
            return answer(200,
                          "{\"upload\":\"" + part.upload + "\",\"next\":" + std::to_string(part.next) + ",\"pieces\":" + std::to_string(part.pieces) +
                              ",\"pieceBytes\":" + std::to_string(part.piece_bytes) + "}",
                          {});
        }

        static std::uint64_t ReadLittleEndian(const std::string& bytes, std::size_t offset, std::size_t size)
        {
            std::uint64_t value = 0;
            for (std::size_t i = size; i > 0; --i)
                value = (value << 8) | static_cast<std::uint8_t>(bytes[offset + i - 1]);
            return value;
        }

        static unsigned WindowLogFor(std::int64_t piece_bytes)
        {
            unsigned log = 10; // zstd's smallest window
            while ((std::int64_t{ 1 } << log) < piece_bytes)
                ++log;
            return log;
        }

        // Exactly one zstd frame over every byte, stating its content size, equal to `length`, with a
        // window of at most `piece_bytes` rounded up to a power of two: decompressed into `out`.
        static bool ReadZstdFrame(const char* data, std::size_t size, std::size_t length, std::int64_t piece_bytes, std::string& out, std::string& why)
        {
            if (size < 4 || ReadLittleEndian(std::string(data, 4), 0, 4) != ZSTD_MAGICNUMBER)
            {
                why = "not a zstd frame";
                return false;
            }
            if (ZSTD_findFrameCompressedSize(data, size) != size)
            {
                why = "not exactly one zstd frame";
                return false;
            }
            const unsigned long long content = ZSTD_getFrameContentSize(data, size);
            if (content == ZSTD_CONTENTSIZE_UNKNOWN || content == ZSTD_CONTENTSIZE_ERROR || content != length)
            {
                why = "the zstd frame does not state the piece's length";
                return false;
            }
            std::unique_ptr<ZSTD_DCtx, size_t (*)(ZSTD_DCtx*)> context(ZSTD_createDCtx(), ZSTD_freeDCtx);
            if (!context || ZSTD_isError(ZSTD_DCtx_setParameter(context.get(), ZSTD_d_windowLogMax, static_cast<int>(WindowLogFor(piece_bytes)))))
            {
                why = "no zstd context";
                return false;
            }
            out.assign(length, '\0');
            const std::size_t got = ZSTD_decompressDCtx(context.get(), length > 0 ? &out[0] : nullptr, length, data, size);
            if (ZSTD_isError(got) || got != length)
            {
                why = "the piece does not decompress";
                return false;
            }
            return true;
        }

        FileSideAction OnFrame(int connection, std::string frame)
        {
            const std::size_t header = frame.size() >= 8 ? static_cast<std::size_t>(ReadLittleEndian(frame, 4, 4)) : 0;
            if (frame.size() < kHeaderBytes || frame[0] != 1 || (frame[1] != 1 && frame[1] != 2) || frame[2] != 0 || frame[3] != 0 || header < kHeaderBytes ||
                header > 1024 || header > frame.size())
                return FileSideAction::CloseWith(1002, "a binary frame without a trusted header");
            if (static_cast<std::int64_t>(frame.size()) > max_frame_bytes_)
                return FileSideAction::CloseWith(1009, "a frame over maxFrameBytes");
            const std::int64_t id = static_cast<std::int64_t>(ReadLittleEndian(frame, 8, 8));

            if (frame[1] == 2)
            {
                for (std::size_t i = 16; i < header; ++i)
                    if (frame[i] != 0)
                        return FileSideAction::CloseWith(1002, "a probe with bytes after its request id");
                probes_.push_back({ connection, id });
                return FileSideAction::ReplyWith(Ack(id, 200, "{}", {}));
            }
            if (frame[52] != 0 || frame[53] != 0 || frame[54] != 0 || frame[55] != 0)
                return FileSideAction::CloseWith(1002, "a piece with a reserved byte set");
            if (frame.size() > kSmallFrameBytes && HeldBy(connection) == nullptr)
                return FileSideAction::CloseWith(1002, "a frame over 64 KiB on a connection that holds no upload");

            if (corrupt_crcs_ > 0)
            {
                --corrupt_crcs_;
                frame[48] = static_cast<char>(frame[48] ^ 0x5a);
            }

            PieceRecord record;
            record.connection = connection;
            record.id = id;
            record.upload = collector::large_files::ToHex(reinterpret_cast<const std::uint8_t*>(frame.data()) + 16, 16);
            record.number = static_cast<std::int64_t>(ReadLittleEndian(frame, 32, 8));
            record.decompressed_length = static_cast<std::uint32_t>(ReadLittleEndian(frame, 40, 4));
            record.compressed_length = static_cast<std::uint32_t>(ReadLittleEndian(frame, 44, 4));
            record.frame_bytes = frame.size();
            const std::uint32_t crc = static_cast<std::uint32_t>(ReadLittleEndian(frame, 48, 4));
            pieces_.push_back(record);
            int& status = pieces_.back().status;
            std::string name;
            for (const auto& part : parts_)
                if (part.second.upload == record.upload)
                    name = part.second.name;
            FileSideAction action = Apply(piece_answers_, id, status, connection, [&] { return PieceReal(connection, id, frame, header, record, crc, status); });
            if (after_piece_)
            {
                try
                {
                    after_piece_(name, record.number);
                }
                catch (...)
                {
                }
            }
            return action;
        }

        FileSideAction PieceReal(int connection, std::int64_t id, const std::string& frame, std::size_t header, const PieceRecord& record, std::uint32_t crc,
                                 int& status)
        {
            const auto answer = [&](int code, const std::string& body_json, const std::string& error, int retry_after = -1) {
                status = code;
                return FileSideAction::ReplyWith(Ack(id, code, body_json, error, retry_after));
            };
            Part* part = HeldBy(connection);
            if (part == nullptr || part->upload != record.upload)
                return answer(404, {}, "no such upload on this connection");
            const std::string piece_name = "piece " + std::to_string(record.number);
            if (record.number < 0 || record.number >= part->pieces)
            {
                Remove(connection);
                return answer(400, {}, piece_name + ": no such piece: the upload is discarded");
            }
            const std::size_t compressed = frame.size() - header;
            if (static_cast<std::int64_t>(record.decompressed_length) != ExpectedLength(*part, record.number) || record.compressed_length != compressed ||
                compressed > ZSTD_compressBound(record.decompressed_length))
            {
                Remove(connection);
                return answer(400, {}, piece_name + ": its lengths are wrong: the upload is discarded");
            }
            const auto* bytes = reinterpret_cast<const Bytef*>(frame.data() + header);
            if (static_cast<std::uint32_t>(crc32_z(0L, compressed > 0 ? bytes : Z_NULL, compressed)) != crc)
                return answer(409, "{\"next\":" + std::to_string(part->next) + ",\"reason\":\"checksum mismatch\"}", "checksum mismatch");
            std::string original;
            std::string why;
            if (!ReadZstdFrame(frame.data() + header, compressed, record.decompressed_length, part->piece_bytes, original, why))
            {
                Remove(connection);
                return answer(400, {}, piece_name + ": " + why + ": the upload is discarded");
            }
            if (record.number < part->next)
                return answer(200, "{\"next\":" + std::to_string(part->next) + "}", {});
            if (record.number > part->next)
                return answer(409, "{\"next\":" + std::to_string(part->next) + ",\"reason\":\"out of order\"}", "out of order");
            collector::large_files::Sha256Digest digest{};
            std::memcpy(digest.data(), frame.data() + 56, digest.size());
            part->data.push_back(std::move(original));
            part->digests.push_back(digest);
            ++part->next;
            return answer(200, "{\"next\":" + std::to_string(part->next) + "}", {});
        }

        FileSideAction OnEnd(int connection, std::int64_t id, const nlohmann::json& body, const std::string& raw)
        {
            EndRecord record;
            record.connection = connection;
            record.id = id;
            record.raw = raw;
            bool upload_ok = false, hash_ok = false;
            record.upload = StringField(body, "upload", upload_ok);
            record.hash = StringField(body, "hash", hash_ok);
            ends_.push_back(record);
            int& status = ends_.back().status;
            return Apply(end_answers_, id, status, connection, [&] { return EndReal(connection, id, record, upload_ok, hash_ok, status); });
        }

        FileSideAction EndReal(int connection, std::int64_t id, const EndRecord& record, bool upload_ok, bool hash_ok, int& status)
        {
            const auto answer = [&](int code, const std::string& body_json, const std::string& error, int retry_after = -1) {
                status = code;
                return FileSideAction::ReplyWith(Ack(id, code, body_json, error, retry_after));
            };
            // An end answered before, asked again: the same answer.
            const auto remembered = [&](int code, const std::string& body_json, const std::string& error) {
                last_end_[connection] = EndAnswer{ record.upload, code, body_json, error };
                return answer(code, body_json, error);
            };
            if (!upload_ok || !IsHex(record.upload, 32) || !hash_ok || !IsHex(record.hash, 64))
                return answer(400, {}, "a malformed end");
            Part* part = HeldBy(connection);
            if (part == nullptr || part->upload != record.upload)
            {
                // The real server lets the upload go when the check begins and answers a repeated end from
                // the check: still running, stored, pieces missing (the part is not held: begin again) or failed.
                const auto check = checks_.find(connection);
                if (check != checks_.end() && check->second == record.upload)
                {
                    Part* checked = PartWithUpload(record.upload);
                    if (checked != nullptr && checked->checking)
                    {
                        if (checking_rounds_ > 0)
                        {
                            --checking_rounds_;
                            return answer(202, "{\"upload\":\"" + checked->upload + "\",\"state\":\"checking\"}", {}, 1);
                        }
                        return CheckAnswer(connection, *checked, remembered);
                    }
                    if (checked != nullptr && checked->stored)
                        return remembered(200, StoredBody(checked->upload, true), {});
                }
                const auto last = last_end_.find(connection);
                if (last != last_end_.end() && last->second.upload == record.upload)
                    return answer(last->second.status, last->second.body, last->second.error);
                return answer(404, {}, "no such upload on this connection");
            }
            if (part->next < part->pieces)
                return answer(409, "{\"next\":" + std::to_string(part->next) + ",\"reason\":\"pieces are missing\"}", "pieces are missing");
            part->end_hash = Lower(record.hash);
            // The check begins: the connection holds no upload from here on, as on the real server.
            part->checking = true;
            part->holder = 0;
            held_.erase(connection);
            checks_[connection] = part->upload;
            if (checking_rounds_ > 0)
            {
                --checking_rounds_;
                return answer(202, "{\"upload\":\"" + part->upload + "\",\"state\":\"checking\"}", {}, 1);
            }
            return CheckAnswer(connection, *part, remembered);
        }

        enum class CheckOutcome
        {
            Stored,
            Missing,
            Failed,
        };

        // The end check of a part (its `checking` set): stored; pieces from `check_missing_from_` missing when
        // the script says so (the part keeps the others and waits); or failed (the part is removed).
        CheckOutcome FinishCheck(Part& part, std::string& error)
        {
            if (check_missing_from_ >= 0)
            {
                const std::int64_t from = (std::min)(check_missing_from_, part.next);
                check_missing_from_ = -1;
                part.checking = false;
                part.next = from;
                part.data.resize(static_cast<std::size_t>(from));
                part.digests.resize(static_cast<std::size_t>(from));
                return CheckOutcome::Missing;
            }
            return CheckAndStore(part, error) ? CheckOutcome::Stored : CheckOutcome::Failed;
        }

        template <class Remembered>
        FileSideAction CheckAnswer(int connection, Part& part, const Remembered& remembered)
        {
            std::string error;
            const std::string upload = part.upload;
            switch (FinishCheck(part, error))
            {
            case CheckOutcome::Stored:
                return remembered(200, StoredBody(upload, true), {});
            case CheckOutcome::Missing:
                checks_.erase(connection);
                return remembered(409, "{\"next\":" + std::to_string(part.next) + ",\"reason\":\"pieces are missing\"}", "pieces are missing");
            case CheckOutcome::Failed:
                break;
            }
            for (auto it = parts_.begin(); it != parts_.end(); ++it)
                if (it->second.upload == upload)
                {
                    parts_.erase(it);
                    break;
                }
            checks_.erase(connection);
            return remembered(400, {}, error);
        }

        Part* PartWithUpload(const std::string& upload)
        {
            for (auto& part : parts_)
                if (part.second.upload == upload)
                    return &part.second;
            return nullptr;
        }

        static std::string StoredBody(const std::string& upload, bool with_time)
        {
            return "{\"upload\":\"" + upload + "\",\"state\":\"stored\"" + (with_time ? std::string(",\"time\":\"") + kStoredTime + "\"" : std::string()) + "}";
        }

        // The end check: every piece's SHA-256 against its digest, then the hash of the digests against
        // the end's. Stores the file when both hold; false with the error of a 400 otherwise.
        bool CheckAndStore(Part& part, std::string& error)
        {
            part.checking = false;
            std::string content;
            std::string digests;
            for (std::size_t k = 0; k < part.data.size(); ++k)
            {
                collector::large_files::Sha256Digest actual{};
                collector::large_files::Sha256Of(part.data[k].data(), part.data[k].size(), actual);
                if (actual != part.digests[k])
                {
                    error = "piece " + std::to_string(k) + ": its bytes do not match its digest: the upload is discarded";
                    return false;
                }
                content += part.data[k];
                digests.append(reinterpret_cast<const char*>(part.digests[k].data()), part.digests[k].size());
            }
            collector::large_files::Sha256Digest hash{};
            collector::large_files::Sha256Of(digests.data(), digests.size(), hash);
            if (collector::large_files::ToHex(hash.data(), hash.size()) != part.end_hash)
            {
                error = "the hash does not match the pieces: the upload is discarded";
                return false;
            }
            part.stored = true;
            part.holder = 0;
            part.data.clear();
            stored_files_[part.name] = std::move(content);
            return true;
        }

        FileSideAction OnDiscard(int connection, std::int64_t id, const nlohmann::json& body, const std::string& raw)
        {
            DiscardRecord record;
            record.connection = connection;
            record.id = id;
            record.raw = raw;
            bool upload_ok = false;
            record.upload = StringField(body, "upload", upload_ok);
            discards_.push_back(record);
            int& status = discards_.back().status;

            const auto answer = [&](int code, const std::string& body_json, const std::string& error, int retry_after = -1) {
                status = code;
                return FileSideAction::ReplyWith(Ack(id, code, body_json, error, retry_after));
            };
            if (discard_unknown_)
                return answer(400, {}, "unknown op");
            return Apply(discard_answers_, id, status, connection, [&] { return DiscardReal(connection, record, upload_ok, answer); });
        }

        template <class Answer>
        FileSideAction DiscardReal(int connection, const DiscardRecord& record, bool upload_ok, const Answer& answer)
        {
            if (!upload_ok || !IsHex(record.upload, 32))
                return answer(400, {}, "a malformed discard");
            Part* part = HeldBy(connection);
            if (part == nullptr || part->upload != record.upload)
            {
                // The end of this connection began a check that still runs: 409 until it settles.
                const auto check = checks_.find(connection);
                Part* checked = check != checks_.end() && check->second == record.upload ? PartWithUpload(record.upload) : nullptr;
                if (checked != nullptr && checked->checking)
                    return answer(409, "{\"state\":\"checking\"}", "checking", 1);
                return answer(404, {}, "no such upload on this connection");
            }
            if (part->checking)
                return answer(409, "{\"state\":\"checking\"}", "checking", 1);
            Remove(connection);
            last_end_.erase(connection);
            checks_.erase(connection);
            return answer(200, "{\"state\":\"discarded\"}", {});
        }

        mutable std::mutex mutex_;
        std::int64_t max_frame_bytes_ = 0;
        int heartbeat_seconds_ = 30;
        std::int64_t max_file_bytes_ = 0;
        std::int64_t piece_bytes_ = 0;
        std::string hello_override_;
        std::deque<FileScriptAnswer> begin_answers_;
        std::deque<FileScriptAnswer> piece_answers_;
        std::deque<FileScriptAnswer> end_answers_;
        std::deque<FileScriptAnswer> discard_answers_;
        int checking_rounds_ = 0;
        std::int64_t check_missing_from_ = -1;
        bool discard_unknown_ = false;
        int corrupt_crcs_ = 0;
        std::uint64_t uploads_ = 0;
        std::map<std::string, std::pair<std::string, std::int64_t>> held_named_; // name -> content, pieces
        std::function<void(const std::string&, std::int64_t)> after_piece_;

        std::map<std::string, Part> parts_; // by KeyOf
        std::map<int, std::string> held_;   // connection -> the key of the part it holds
        std::map<int, EndAnswer> last_end_; // connection -> its last answered end
        std::map<int, std::string> checks_; // connection -> the upload whose check its end began
        std::map<std::string, std::string> stored_files_;
        std::set<std::int64_t> unanswered_;
        int pings_ = 0;

        std::vector<BeginRecord> begins_;
        std::vector<PieceRecord> pieces_;
        std::vector<EndRecord> ends_;
        std::vector<DiscardRecord> discards_;
        std::vector<ProbeRecord> probes_;
        std::vector<ConnectionRecord> connections_;
    };
} // namespace hsm::test
