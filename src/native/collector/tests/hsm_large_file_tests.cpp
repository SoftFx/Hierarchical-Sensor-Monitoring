// Large file values of the native collector (#1887, ADR-0022): the file connection's answers, SHA-256,
// the platform layer, the state files, the uploader and the C ABI. Compiled in every build; what needs
// the file connection (zstd, zlib, the session) is compiled with HSM_COLLECTOR_LARGE_FILES only.
//
// No test decides by time: settle times and scans run on a manual clock and a scan seam, and a wait only
// fails a test when it expires.

#include "hsm_collector/hsm_collector.h"

#if defined(HSM_COLLECTOR_LARGE_FILES)
#include "../src/large_files/hsm_file_connection.hpp"
#include "../src/large_files/hsm_sha256.hpp"
#endif

#include <cstdint>
#include <functional>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
    void Check(bool condition, const std::string& message)
    {
        if (!condition)
            throw std::runtime_error(message);
    }

#if defined(HSM_COLLECTOR_LARGE_FILES)
    namespace lf = hsm::collector::large_files;

    std::string HexOf(const std::string& text)
    {
        lf::Sha256Digest digest{};
        Check(lf::Sha256Of(text.data(), text.size(), digest), "SHA-256 must succeed");
        return lf::ToHex(digest.data(), digest.size());
    }

    // The FIPS 180-2 vectors, through the library of the platform (CNG or libcrypto), in one call and in
    // pieces.
    void NativeLargeFileSha256MatchesTheStandardVectors()
    {
        Check(HexOf("") == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "SHA-256 of nothing");
        Check(HexOf("abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256 of abc");
        const std::string two = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        Check(HexOf(two) == "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1", "SHA-256 of the two-block vector");
        lf::Sha256 running;
        for (char c : two)
            running.Update(&c, 1);
        lf::Sha256Digest digest{};
        Check(running.Final(digest) && lf::ToHex(digest.data(), digest.size()) == HexOf(two), "a running hash must equal the one-shot hash");
        Check(!running.Final(digest), "a spent hash must refuse a second Final");
    }

    void NativeLargeFileModifiedIsWrittenAsTheWireAsks()
    {
        Check(lf::FileConnection::FormatTicks(0) == "1970-01-01T00:00:00.0000000Z", "the epoch");
        // 2026-11-25T10:00:00.1234567Z
        const std::int64_t ticks = (static_cast<std::int64_t>(1795600800) * 10000000) + 1234567;
        Check(lf::FileConnection::FormatTicks(ticks) == "2026-11-25T10:00:00.1234567Z", "a time with 7 fractional digits: " + lf::FileConnection::FormatTicks(ticks));
        Check(lf::FileConnection::FormatTicks(-1) == "1969-12-31T23:59:59.9999999Z", "a time before 1970");
    }

    hsm::http::AckedLinkAck Ack(long status, const std::string& body = std::string(), const std::string& error = std::string(), std::int64_t retry = -1)
    {
        hsm::http::AckedLinkAck ack;
        ack.id = 1;
        ack.status = status;
        ack.body = body;
        ack.error = error;
        ack.retry_after_seconds = retry;
        return ack;
    }

    // Every status of the wire contract's tables, read as the uploader acts on it.
    void NativeLargeFileEveryAnswerMapsToItsKind()
    {
        using K = lf::FileAnswerKind;
        const std::string upload = "\"upload\":\"0123456789abcdef0123456789abcdef\"";
        const auto kind = [](const char* op, const hsm::http::AckedLinkAck& ack) { return lf::FileConnection::Read(op, ack).kind; };

        // begin
        const auto next = lf::FileConnection::Read("begin", Ack(200, "{" + upload + ",\"next\":3,\"pieces\":30,\"pieceBytes\":4173824}"));
        Check(next.kind == K::Next && next.next == 3 && next.pieces == 30 && next.piece_bytes == 4173824 && next.upload.size() == 32, "begin 200 next");
        Check(kind("begin", Ack(200, "{" + upload + ",\"state\":\"stored\"}")) == K::Stored, "begin 200 stored");
        const auto checking = lf::FileConnection::Read("begin", Ack(202, "{" + upload + ",\"state\":\"checking\"}", "", 5));
        Check(checking.kind == K::Checking && checking.retry_after_ms == 5000, "begin 202 checking with retryAfter");
        Check(kind("begin", Ack(503, "", "no room", 60)) == K::RetryLater, "begin 503");
        Check(kind("begin", Ack(409, "", "busy", 2)) == K::RetryLater, "begin 409 busy");
        Check(kind("begin", Ack(413, "", "too large")) == K::Final, "begin 413");
        Check(kind("begin", Ack(422, "", "cannot take")) == K::Final, "begin 422");
        Check(kind("begin", Ack(400, "", "malformed")) == K::Final, "begin 400");
        Check(kind("begin", Ack(500, "", "fault")) == K::Fault, "begin 500");
        Check(kind("begin", Ack(200, "{" + upload + ",\"next\":31,\"pieces\":30,\"pieceBytes\":4173824}")) == K::Fault, "a begin answer with next past pieces cannot be used");
        Check(lf::FileConnection::Read("begin", Ack(503, "", "x", 100000)).retry_after_ms == lf::kMaxRetryAfterMs, "retryAfter is capped at 5 minutes");
        Check(lf::FileConnection::Read("begin", Ack(503, "", "x", 0)).retry_after_ms == 1000, "retryAfter is at least 1 s");

        // piece
        Check(lf::FileConnection::Read("piece", Ack(200, "{\"next\":4}")).next == 4, "piece 200 next");
        Check(kind("piece", Ack(409, "{\"next\":4,\"reason\":\"checksum mismatch\"}")) == K::ChecksumMismatch, "piece 409 checksum");
        const auto order = lf::FileConnection::Read("piece", Ack(409, "{\"next\":2,\"reason\":\"out of order\"}"));
        Check(order.kind == K::Next && order.next == 2, "piece 409 out of order goes on from the server's next");
        Check(kind("piece", Ack(503, "", "write failed", 3)) == K::RetryLater, "piece 503");
        Check(kind("piece", Ack(404, "", "no upload")) == K::NotHeld, "piece 404");
        Check(kind("piece", Ack(400, "", "piece 3: the upload is discarded")) == K::Discarded, "piece 400");

        // end
        const auto stored = lf::FileConnection::Read("end", Ack(200, "{" + upload + ",\"state\":\"stored\",\"time\":\"2026-11-25T10:00:00.1234567Z\"}"));
        Check(stored.kind == K::Stored && stored.stored_ticks == (static_cast<std::int64_t>(1795600800) * 10000000) + 1234567, "end 200 stored with its time");
        Check(kind("end", Ack(202, "{" + upload + ",\"state\":\"checking\"}", "", 5)) == K::Checking, "end 202");
        Check(lf::FileConnection::Read("end", Ack(409, "{\"next\":7,\"reason\":\"pieces are missing\"}")).next == 7, "end 409 missing");
        Check(kind("end", Ack(409, "{\"next\":7,\"reason\":\"pieces are missing\"}")) == K::Missing, "end 409 kind");
        Check(kind("end", Ack(400, "", "hash mismatch: the upload is discarded")) == K::Discarded, "end 400");
        Check(kind("end", Ack(422, "", "sensor gone")) == K::Final, "end 422");
        Check(kind("end", Ack(404, "", "no upload")) == K::NotHeld, "end 404");
        Check(kind("end", Ack(503, "", "cannot read", 5)) == K::RetryLater, "end 503");

        // discard
        Check(kind("discard", Ack(200, "{\"state\":\"discarded\"}")) == K::DiscardDone, "discard 200");
        Check(kind("discard", Ack(404, "", "no upload")) == K::NotHeld, "discard 404");
        Check(kind("discard", Ack(409, "{\"state\":\"checking\"}", "", 5)) == K::Checking, "discard 409 checking");
        Check(kind("discard", Ack(503, "", "fault", 5)) == K::RetryLater, "discard 503");
        Check(kind("discard", Ack(400, "", "unknown op")) == K::UnknownOp, "discard on a server before the op");
    }

    void NativeLargeFileHelloIsChecked()
    {
        lf::FileHello hello;
        std::string why;
        Check(lf::FileConnection::ReadHello("{\"maxFrameBytes\":4194304,\"heartbeatSeconds\":30,\"fileLimits\":{\"maxFileBytes\":68719476736,\"pieceBytes\":4173824}}", hello, why),
              "the default hello: " + why);
        Check(hello.piece_bytes == 4173824 && hello.max_file_bytes == 68719476736LL && hello.max_frame_bytes == 4194304, "the hello's numbers");
        Check(!lf::FileConnection::ReadHello("{\"maxFrameBytes\":4194304,\"heartbeatSeconds\":30}", hello, why), "a hello without fileLimits");
        Check(!lf::FileConnection::ReadHello("{\"maxFrameBytes\":4194304,\"fileLimits\":{\"maxFileBytes\":1,\"pieceBytes\":4194304}}", hello, why),
              "a piece that cannot fit the frame");
        Check(!lf::FileConnection::ReadHello("{\"maxFrameBytes\":4194304,\"fileLimits\":{\"maxFileBytes\":1,\"pieceBytes\":4096}}", hello, why),
              "a piece below 64 KiB");
    }
#endif
} // namespace

const std::map<std::string, std::function<void(const std::string&)>>& LargeFileTests()
{
    static const std::map<std::string, std::function<void(const std::string&)>> tests = {
#if defined(HSM_COLLECTOR_LARGE_FILES)
        { "native_large_file_sha256_matches_the_standard_vectors", [](const std::string&) { NativeLargeFileSha256MatchesTheStandardVectors(); } },
        { "native_large_file_modified_is_written_as_the_wire_asks", [](const std::string&) { NativeLargeFileModifiedIsWrittenAsTheWireAsks(); } },
        { "native_large_file_every_answer_maps_to_its_kind", [](const std::string&) { NativeLargeFileEveryAnswerMapsToItsKind(); } },
        { "native_large_file_hello_is_checked", [](const std::string&) { NativeLargeFileHelloIsChecked(); } },
#endif
    };
    return tests;
}
