#pragma once

/// @file
/// @brief The file connection of the collector session (#1887; wire contract "File connection", #1884):
/// a third WebSocket of the session with the subprotocol hsm.files.v1, carried by AckedBinaryLink. The
/// requests `begin`, `end` and `discard` are text, a piece is a binary frame with an 88-byte header; every
/// request gets one ack. This class builds the requests and reads the answers into FileAnswer; what to do
/// with an answer is the uploader's. Internal; compiled with HSM_COLLECTOR_HTTP and HSM_COLLECTOR_TABLES.
///
/// Threads: one owner thread (the uploader) calls everything but Cancel, which any thread may call.
/// Nothing throws.

#include "../hsm_acked_binary_link.hpp"
#include "hsm_sha256.hpp"

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace hsm::collector::large_files
{
    /// The bytes in front of the zstd frame of a piece.
    constexpr std::size_t kPieceHeaderBytes = 88;
    /// The smallest piece a `begin` may name.
    constexpr std::int64_t kMinPieceBytes = 65536;
    /// The longest `retryAfter` honoured (both collectors): 5 minutes.
    constexpr std::int64_t kMaxRetryAfterMs = 5 * 60 * 1000;
    /// The longest comment a begin carries, in UTF-16 code units as the server counts it (.NET characters);
    /// a longer one is cut at a code point and ends with an ellipsis (U+2026), so the server never refuses it.
    constexpr std::size_t kMaxBeginTextUnits = 4096;
    /// The longest `path` and `name` the server takes in a begin, in UTF-16 code units (400 beyond).
    constexpr std::size_t kMaxBeginNameUnits = 1024;

    /// The hello of a file connection.
    struct FileHello
    {
        std::int64_t max_frame_bytes = 0;
        std::int64_t heartbeat_ms = 0;
        std::int64_t max_file_bytes = 0;
        std::int64_t piece_bytes = 0;
    };

    /// What names a file to the server (the body of `begin`).
    struct FileFacts
    {
        /// The File sensor's registered path.
        std::string path;
        /// The file's name (UTF-8), no folder.
        std::string name;
        std::int64_t size = 0;
        /// The modification time in 100 ns ticks since 1970-01-01 UTC.
        std::int64_t modified_ticks = 0;
        /// The opaque identity of the source file; empty: not sent.
        std::string source;
        /// Sent in every begin: a secure file is kept under the product's dump password.
        bool secure = true;
        /// A routed send's value status (the wire's SensorStatus number) and comment; -1 and empty: not sent.
        int status = -1;
        std::string comment;
    };

    /// How the uploader reads one answer.
    enum class FileAnswerKind
    {
        /// begin 200, a piece 200, a piece 409 out of order: go on from `next` (`pieces`, `piece_bytes`
        /// and `upload` set for a begin).
        Next,
        /// begin or end 200 "stored": the file is a value on the server.
        Stored,
        /// 202 "checking" (begin, end) or 409 "checking" (discard): ask again after `retry_after_ms`.
        Checking,
        /// 503, or 409 busy at begin: nothing changed; the same request again after `retry_after_ms`.
        RetryLater,
        /// A piece's 409 "checksum mismatch": the same piece again.
        ChecksumMismatch,
        /// end 409 "pieces are missing". The connection may no longer hold the upload (the server lets it
        /// go when its check begins), so the client begins again and goes on from the piece begin names.
        Missing,
        /// 400 "...: the upload is discarded" on a piece or at end: the part is gone; begin from 0.
        Discarded,
        /// discard 200: the part, its reservation and its slot are gone.
        DiscardDone,
        /// 404: the connection holds no such upload; begin again.
        NotHeld,
        /// A final refusal for this file until the next Start (413, 422, a 400 at begin, other 4xx; at a
        /// piece a 4xx other than 400, 404 and 409).
        Final,
        /// The request could not be built (a name or comment that is not valid UTF-8): nothing was sent;
        /// final for this file.
        BadRequest,
        /// discard answered 400 "unknown op": a server before the discard op; the part expires there.
        UnknownOp,
        /// A 5xx without retryAfter or an answer this version cannot read: try again after a backoff.
        Fault,
        /// No answer: the connection ended (LastEnd says how) or no ack came in time.
        Lost,
        /// Cancel was called.
        Cancelled,
        /// No connection is open.
        NotOpen,
    };

    struct FileAnswer
    {
        FileAnswerKind kind = FileAnswerKind::Lost;
        long status = 0;
        std::string upload;
        std::int64_t next = -1;
        std::int64_t pieces = -1;
        std::int64_t piece_bytes = -1;
        /// -1 when the answer carried none (the caller picks its own wait).
        std::int64_t retry_after_ms = -1;
        /// The server's error text, or why there was no answer.
        std::string error;
        /// The value's time of an end answered "stored", in 100 ns ticks since 1970; 0 otherwise.
        std::int64_t stored_ticks = 0;
    };

    struct FileConnectionOptions
    {
        static constexpr const char* kSubprotocol = "hsm.files.v1";

        std::string url;
        std::vector<hsm::http::HttpHeader> headers;
        bool verify_peer = true;
        std::int64_t open_timeout_ms = 15000;
        /// How long a request waits for its ack once written.
        std::int64_t request_timeout_ms = 30000;
        /// For tests: see AckedLinkOptions.
        std::function<bool(std::int64_t request_id)> ack_abandoned_for_tests;
    };

    class FileConnection
    {
    public:
        explicit FileConnection(FileConnectionOptions options);
        ~FileConnection();

        FileConnection(const FileConnection&) = delete;
        FileConnection& operator=(const FileConnection&) = delete;

        /// The upgrade with X-Hsm-Session, the hello (its fileLimits checked) and the probe.
        hsm::http::AckedLinkOpen Open(const std::string& session_id);
        const FileHello& Hello() const noexcept { return hello_; }

        FileAnswer Begin(const FileFacts& facts, std::int64_t piece_bytes);
        /// `frame` holds kPieceHeaderBytes reserved bytes, then exactly one zstd frame of the piece.
        /// The header is written here: the upload id, the number, the lengths, the CRC-32 of the
        /// compressed bytes and the SHA-256 of the piece's original bytes.
        FileAnswer Piece(const std::string& upload, std::int64_t number, std::uint32_t decompressed_length, const Sha256Digest& digest,
                         std::vector<std::uint8_t>& frame);
        FileAnswer End(const std::string& upload, const std::string& hash_hex);
        FileAnswer Discard(const std::string& upload);

        bool Service();
        std::chrono::steady_clock::time_point NextPingDue() const;
        void Close(std::int64_t linger_ms, int code, const std::string& reason);
        void Cancel();
        void ResetCancel();
        bool IsOpen() const;
        const hsm::http::AckedLinkEnd& LastEnd() const;
        std::int64_t SendTimeoutMs() const noexcept;

        /// The answer of one ack of `op` ("begin", "piece", "end", "discard"); public for tests.
        static FileAnswer Read(const char* op, const hsm::http::AckedLinkAck& ack);
        /// The hello check; public for tests.
        static bool ReadHello(const std::string& body, FileHello& hello, std::string& why);
        /// `modified` as the wire writes it: UTC, 7 fractional digits, `Z`.
        static std::string FormatTicks(std::int64_t ticks_since_1970);
        /// 32 lower-case hex digits to 16 bytes; false for anything else.
        static bool ParseUploadId(const std::string& hex, std::uint8_t (&bytes)[16]);
        /// `comment` cut to kMaxBeginTextUnits UTF-16 code units, the ellipsis included, when longer.
        static std::string BoundComment(const std::string& comment);
        /// The length of UTF-8 `text` in UTF-16 code units (a code point above U+FFFF counts 2).
        static std::size_t Utf16Length(const std::string& text);

    private:
        FileAnswer FromPut(const char* op, hsm::http::AckedLinkPutResult result, const hsm::http::AckedLinkAck& ack);

        FileConnectionOptions options_;
        std::unique_ptr<hsm::http::AckedBinaryLink> link_;
        FileHello hello_{};
    };
} // namespace hsm::collector::large_files
