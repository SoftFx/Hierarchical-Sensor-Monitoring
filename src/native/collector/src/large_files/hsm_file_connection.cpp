#include "hsm_file_connection.hpp"

#include <nlohmann/json.hpp>
#include <zlib.h>
#include <zstd.h>

#include <algorithm>
#include <cstdio>
#include <cstring>

namespace hsm::collector::large_files
{
    namespace
    {
        constexpr std::int64_t kTicksPerSecond = 10000000;

        void PutLittleEndian(std::vector<std::uint8_t>& frame, std::size_t offset, std::uint64_t value, std::size_t bytes)
        {
            for (std::size_t i = 0; i < bytes; ++i)
                frame[offset + i] = static_cast<std::uint8_t>((value >> (8 * i)) & 0xFF);
        }

        // Days since 1970-01-01 of a civil date, and back (proleptic Gregorian; H. Hinnant's algorithms).
        std::int64_t DaysFromCivil(std::int64_t y, unsigned m, unsigned d)
        {
            y -= m <= 2;
            const std::int64_t era = (y >= 0 ? y : y - 399) / 400;
            const unsigned yoe = static_cast<unsigned>(y - era * 400);
            const unsigned doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + static_cast<std::int64_t>(doe) - 719468;
        }

        void CivilFromDays(std::int64_t z, std::int64_t& y, unsigned& m, unsigned& d)
        {
            z += 719468;
            const std::int64_t era = (z >= 0 ? z : z - 146096) / 146097;
            const unsigned doe = static_cast<unsigned>(z - era * 146097);
            const unsigned yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            y = static_cast<std::int64_t>(yoe) + era * 400;
            const unsigned doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            const unsigned mp = (5 * doy + 2) / 153;
            d = doy - (153 * mp + 2) / 5 + 1;
            m = mp < 10 ? mp + 3 : mp - 9;
            y += m <= 2;
        }

        // Reads `count` decimal digits at `at`; -1 when one is not a digit.
        int Digits(const std::string& text, std::size_t at, std::size_t count)
        {
            if (at + count > text.size())
                return -1;
            int value = 0;
            for (std::size_t i = at; i < at + count; ++i)
            {
                if (text[i] < '0' || text[i] > '9')
                    return -1;
                value = value * 10 + (text[i] - '0');
            }
            return value;
        }

        // "2026-11-25T12:00:00.0000000Z" (the form the server writes) to ticks since 1970; 0 when it is
        // not that form. A value the collector only reports, never compares.
        std::int64_t ParseTicks(const std::string& text)
        {
            if (text.size() < 20 || text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':')
                return 0;
            const int year = Digits(text, 0, 4), month = Digits(text, 5, 2), day = Digits(text, 8, 2);
            const int hour = Digits(text, 11, 2), minute = Digits(text, 14, 2), second = Digits(text, 17, 2);
            if (year < 0 || month < 1 || month > 12 || day < 1 || day > 31 || hour < 0 || minute < 0 || second < 0)
                return 0;
            std::int64_t ticks = (DaysFromCivil(year, static_cast<unsigned>(month), static_cast<unsigned>(day)) * 86400 + hour * 3600 + minute * 60 + second) * kTicksPerSecond;
            if (text[19] == '.')
            {
                std::int64_t scale = kTicksPerSecond / 10;
                for (std::size_t i = 20; i < text.size() && text[i] >= '0' && text[i] <= '9' && scale > 0; ++i, scale /= 10)
                    ticks += (text[i] - '0') * scale;
            }
            return ticks;
        }

        std::int64_t RetryAfterMs(const hsm::http::AckedLinkAck& ack)
        {
            if (ack.retry_after_seconds < 0)
                return -1;
            const std::int64_t seconds = (std::max<std::int64_t>)(ack.retry_after_seconds, 1);
            return (std::min)(seconds * 1000, kMaxRetryAfterMs);
        }

        nlohmann::json Body(const hsm::http::AckedLinkAck& ack)
        {
            if (ack.body.empty())
                return nlohmann::json::object();
            auto body = nlohmann::json::parse(ack.body, nullptr, /*allow_exceptions=*/false);
            return body.is_object() ? body : nlohmann::json::object();
        }

        std::int64_t IntOf(const nlohmann::json& body, const char* name)
        {
            const auto found = body.find(name);
            if (found == body.end() || !found->is_number_integer())
                return -1;
            return found->get<std::int64_t>();
        }

        std::string StringOf(const nlohmann::json& body, const char* name)
        {
            const auto found = body.find(name);
            if (found == body.end() || !found->is_string())
                return std::string();
            return found->get<std::string>();
        }
    } // namespace

    FileConnection::FileConnection(FileConnectionOptions options)
        : options_(std::move(options))
    {
        hsm::http::AckedLinkOptions link;
        link.url = options_.url;
        link.headers = options_.headers;
        link.verify_peer = options_.verify_peer;
        link.subprotocol = FileConnectionOptions::kSubprotocol;
        link.header_bytes = static_cast<std::uint32_t>(kPieceHeaderBytes);
        link.probe_kind = 2;
        link.open_timeout_ms = options_.open_timeout_ms;
        link.ack_abandoned_for_tests = options_.ack_abandoned_for_tests;
        link_ = std::make_unique<hsm::http::AckedBinaryLink>(std::move(link));
    }

    FileConnection::~FileConnection() = default;

    bool FileConnection::ReadHello(const std::string& body, FileHello& hello, std::string& why)
    {
        const auto json = nlohmann::json::parse(body, nullptr, /*allow_exceptions=*/false);
        if (!json.is_object())
        {
            why = "the file hello is not a JSON object";
            return false;
        }
        hello.max_frame_bytes = IntOf(json, "maxFrameBytes");
        const auto limits = json.find("fileLimits");
        if (limits == json.end() || !limits->is_object())
        {
            why = "the file hello has no fileLimits";
            return false;
        }
        hello.max_file_bytes = IntOf(*limits, "maxFileBytes");
        hello.piece_bytes = IntOf(*limits, "pieceBytes");
        if (hello.max_frame_bytes <= 0 || hello.max_file_bytes < 0 || hello.piece_bytes < kMinPieceBytes)
        {
            why = "the file hello states limits this collector cannot use (maxFrameBytes " + std::to_string(hello.max_frame_bytes) +
                  ", maxFileBytes " + std::to_string(hello.max_file_bytes) + ", pieceBytes " + std::to_string(hello.piece_bytes) + ")";
            return false;
        }
        const std::size_t bound = ZSTD_compressBound(static_cast<std::size_t>(hello.piece_bytes));
        if (ZSTD_isError(bound) || static_cast<std::int64_t>(kPieceHeaderBytes + bound) > hello.max_frame_bytes)
        {
            why = "the file hello's pieceBytes " + std::to_string(hello.piece_bytes) + " does not fit its maxFrameBytes " +
                  std::to_string(hello.max_frame_bytes);
            return false;
        }
        return true;
    }

    hsm::http::AckedLinkOpen FileConnection::Open(const std::string& session_id)
    {
        FileHello hello{};
        auto opened = link_->Open(session_id, [&hello](const std::string& body, std::string& why) { return ReadHello(body, hello, why); });
        if (opened.ok)
        {
            hello.heartbeat_ms = opened.heartbeat_ms;
            hello_ = hello;
        }
        return opened;
    }

    std::int64_t FileConnection::SendTimeoutMs() const noexcept
    {
        // A frame of megabytes on a slow link: the write gets the longer of the request timeout and ten
        // heartbeats (the server's frame deadline is three).
        return (std::max)(options_.request_timeout_ms, 10 * (std::max<std::int64_t>)(link_->HeartbeatMs(), 1000));
    }

    std::string FileConnection::FormatTicks(std::int64_t ticks)
    {
        std::int64_t seconds = ticks / kTicksPerSecond;
        std::int64_t fraction = ticks % kTicksPerSecond;
        if (fraction < 0)
        {
            fraction += kTicksPerSecond;
            --seconds;
        }
        std::int64_t days = seconds / 86400;
        std::int64_t in_day = seconds % 86400;
        if (in_day < 0)
        {
            in_day += 86400;
            --days;
        }
        std::int64_t year = 0;
        unsigned month = 0, day = 0;
        CivilFromDays(days, year, month, day);
        char text[48];
        std::snprintf(text, sizeof text, "%04lld-%02u-%02uT%02lld:%02lld:%02lld.%07lldZ", static_cast<long long>(year), month, day,
                      static_cast<long long>(in_day / 3600), static_cast<long long>((in_day / 60) % 60), static_cast<long long>(in_day % 60),
                      static_cast<long long>(fraction));
        return text;
    }

    bool FileConnection::ParseUploadId(const std::string& hex, std::uint8_t (&bytes)[16])
    {
        if (hex.size() != 32)
            return false;
        for (std::size_t i = 0; i < 16; ++i)
        {
            int value = 0;
            for (std::size_t k = 0; k < 2; ++k)
            {
                const char c = hex[2 * i + k];
                int digit = -1;
                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'a' && c <= 'f')
                    digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F')
                    digit = c - 'A' + 10;
                if (digit < 0)
                    return false;
                value = value * 16 + digit;
            }
            bytes[i] = static_cast<std::uint8_t>(value);
        }
        return true;
    }

    FileAnswer FileConnection::Read(const char* op, const hsm::http::AckedLinkAck& ack)
    {
        FileAnswer answer;
        answer.status = ack.status;
        answer.error = ack.error;
        answer.retry_after_ms = RetryAfterMs(ack);
        const auto body = Body(ack);
        answer.upload = StringOf(body, "upload");
        answer.next = IntOf(body, "next");
        answer.pieces = IntOf(body, "pieces");
        answer.piece_bytes = IntOf(body, "pieceBytes");
        const std::string state = StringOf(body, "state");
        const std::string reason = StringOf(body, "reason");
        const std::string what(op);
        const long s = ack.status;

        if (what == "begin")
        {
            if (s == 200 && state == "stored")
                answer.kind = FileAnswerKind::Stored;
            else if (s == 200 && answer.next >= 0 && answer.pieces >= 0 && answer.next <= answer.pieces && answer.piece_bytes >= kMinPieceBytes &&
                     answer.upload.size() == 32)
                answer.kind = FileAnswerKind::Next;
            else if (s == 202)
                answer.kind = FileAnswerKind::Checking;
            else if (s == 503 || (s == 409 && answer.retry_after_ms >= 0))
                answer.kind = FileAnswerKind::RetryLater;
            else if (s >= 400 && s < 500)
                answer.kind = FileAnswerKind::Final;
            else
                answer.kind = FileAnswerKind::Fault;
        }
        else if (what == "piece")
        {
            if (s == 200 && answer.next >= 0)
                answer.kind = FileAnswerKind::Next;
            else if (s == 409 && reason == "checksum mismatch")
                answer.kind = FileAnswerKind::ChecksumMismatch;
            else if (s == 409 && answer.next >= 0)
                answer.kind = FileAnswerKind::Next; // "out of order": go on from the server's next
            else if (s == 503)
                answer.kind = FileAnswerKind::RetryLater;
            else if (s == 404)
                answer.kind = FileAnswerKind::NotHeld;
            else if (s == 400)
                answer.kind = FileAnswerKind::Discarded;
            else
                answer.kind = FileAnswerKind::Fault;
        }
        else if (what == "end")
        {
            if (s == 200 && state == "stored")
            {
                answer.kind = FileAnswerKind::Stored;
                answer.stored_ticks = ParseTicks(StringOf(body, "time"));
            }
            else if (s == 202)
                answer.kind = FileAnswerKind::Checking;
            else if (s == 409 && answer.next >= 0)
                answer.kind = FileAnswerKind::Missing;
            else if (s == 400)
                answer.kind = FileAnswerKind::Discarded;
            else if (s == 404)
                answer.kind = FileAnswerKind::NotHeld;
            else if (s == 503)
                answer.kind = FileAnswerKind::RetryLater;
            else if (s >= 400 && s < 500)
                answer.kind = FileAnswerKind::Final;
            else
                answer.kind = FileAnswerKind::Fault;
        }
        else // discard
        {
            if (s == 200)
                answer.kind = FileAnswerKind::DiscardDone;
            else if (s == 404)
                answer.kind = FileAnswerKind::NotHeld;
            else if (s == 409)
                answer.kind = FileAnswerKind::Checking;
            else if (s == 503)
                answer.kind = FileAnswerKind::RetryLater;
            else if (s == 400 && ack.error.find("unknown op") != std::string::npos)
                answer.kind = FileAnswerKind::UnknownOp;
            else
                answer.kind = FileAnswerKind::Fault;
        }
        return answer;
    }

    FileAnswer FileConnection::FromPut(const char* op, hsm::http::AckedLinkPutResult result, const hsm::http::AckedLinkAck& ack)
    {
        switch (result)
        {
        case hsm::http::AckedLinkPutResult::Acked:
            return Read(op, ack);
        case hsm::http::AckedLinkPutResult::Cancelled: {
            FileAnswer answer;
            answer.kind = FileAnswerKind::Cancelled;
            answer.error = "cancelled";
            return answer;
        }
        case hsm::http::AckedLinkPutResult::NotOpen: {
            FileAnswer answer;
            answer.kind = FileAnswerKind::NotOpen;
            answer.error = "the file connection is not open";
            return answer;
        }
        case hsm::http::AckedLinkPutResult::Lost:
            break;
        }
        FileAnswer answer;
        answer.kind = FileAnswerKind::Lost;
        answer.error = link_->LastEnd().why;
        return answer;
    }

    FileAnswer FileConnection::Begin(const FileFacts& facts, std::int64_t piece_bytes)
    {
        try
        {
            nlohmann::json body = nlohmann::json::object();
            body["path"] = facts.path;
            body["name"] = facts.name;
            body["size"] = facts.size;
            body["modified"] = FormatTicks(facts.modified_ticks);
            if (!facts.source.empty())
                body["source"] = facts.source;
            body["pieceBytes"] = piece_bytes;
            hsm::http::AckedLinkAck ack;
            const auto result = link_->Request("begin", body.dump(), SendTimeoutMs(), options_.request_timeout_ms, ack);
            return FromPut("begin", result, ack);
        }
        catch (...)
        {
            FileAnswer answer;
            answer.kind = FileAnswerKind::Fault;
            answer.error = "the begin request could not be built";
            return answer;
        }
    }

    FileAnswer FileConnection::Piece(const std::string& upload, std::int64_t number, std::uint32_t decompressed_length, const Sha256Digest& digest,
                                     std::vector<std::uint8_t>& frame)
    {
        std::uint8_t id[16] = {};
        if (frame.size() < kPieceHeaderBytes || !ParseUploadId(upload, id))
        {
            FileAnswer answer;
            answer.kind = FileAnswerKind::Fault;
            answer.error = "a piece without a frame or an upload id";
            return answer;
        }
        const std::size_t compressed = frame.size() - kPieceHeaderBytes;
        std::memcpy(frame.data() + 16, id, 16);
        PutLittleEndian(frame, 32, static_cast<std::uint64_t>(number), 8);
        PutLittleEndian(frame, 40, decompressed_length, 4);
        PutLittleEndian(frame, 44, static_cast<std::uint32_t>(compressed), 4);
        const auto crc = crc32_z(0L, compressed > 0 ? frame.data() + kPieceHeaderBytes : Z_NULL, compressed);
        PutLittleEndian(frame, 48, static_cast<std::uint32_t>(crc), 4);
        PutLittleEndian(frame, 52, 0, 4);
        std::memcpy(frame.data() + 56, digest.data(), digest.size());

        hsm::http::AckedLinkAck ack;
        const auto result = link_->Put(1, frame, SendTimeoutMs(), options_.request_timeout_ms, ack);
        return FromPut("piece", result, ack);
    }

    FileAnswer FileConnection::End(const std::string& upload, const std::string& hash_hex)
    {
        try
        {
            nlohmann::json body = nlohmann::json::object();
            body["upload"] = upload;
            body["hash"] = hash_hex;
            hsm::http::AckedLinkAck ack;
            const auto result = link_->Request("end", body.dump(), SendTimeoutMs(), options_.request_timeout_ms, ack);
            return FromPut("end", result, ack);
        }
        catch (...)
        {
            FileAnswer answer;
            answer.kind = FileAnswerKind::Fault;
            answer.error = "the end request could not be built";
            return answer;
        }
    }

    FileAnswer FileConnection::Discard(const std::string& upload)
    {
        try
        {
            nlohmann::json body = nlohmann::json::object();
            body["upload"] = upload;
            hsm::http::AckedLinkAck ack;
            const auto result = link_->Request("discard", body.dump(), SendTimeoutMs(), options_.request_timeout_ms, ack);
            return FromPut("discard", result, ack);
        }
        catch (...)
        {
            FileAnswer answer;
            answer.kind = FileAnswerKind::Fault;
            answer.error = "the discard request could not be built";
            return answer;
        }
    }

    bool FileConnection::Service() { return link_->Service(); }
    std::chrono::steady_clock::time_point FileConnection::NextPingDue() const { return link_->NextPingDue(); }
    void FileConnection::Close(std::int64_t linger_ms, int code, const std::string& reason) { link_->Close(linger_ms, code, reason); }
    void FileConnection::Cancel() { link_->Cancel(); }
    void FileConnection::ResetCancel() { link_->ResetCancel(); }
    bool FileConnection::IsOpen() const { return link_->IsOpen(); }
    const hsm::http::AckedLinkEnd& FileConnection::LastEnd() const { return link_->LastEnd(); }
} // namespace hsm::collector::large_files
