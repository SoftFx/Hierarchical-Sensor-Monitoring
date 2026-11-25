#pragma once

/// @file
/// @brief The table channel over the collector session (#1848 part 4): registrations as a `commands`
/// request of their own on the session, blocks on the session's table connection (an AckedBinaryLink
/// with the subprotocol hsm.tables.v1). Internal; compiled with HSM_COLLECTOR_HTTP and
/// HSM_COLLECTOR_TABLES only. The managed counterpart is SessionDataSender.Tables plus TableConnection;
/// the behaviour is that of aicontext/features/collector/table-sensors/feature.md, "The table
/// connection", with the differences its section "Native collector" lists.
///
/// Threads. The pipeline's delivery thread owns it (in tests with a manual tick, the test thread):
/// Register, Put, Service and Close run there, one at a time, and only they touch the connection. The
/// tick thread and the application read MaxBlockBytes and Limits (under a lock of their own); Cancel
/// may come from any thread. There is no thread of its own: the delivery thread wakes for it when
/// ServiceDelayMs says so (to open the connection, to reconnect after a pause, to ping).
///
/// Reports. Put and Register run under locks of the pipeline, so nothing is logged there: lines are
/// queued and WriteReports writes them once no lock is held.

#include "hsm_table_channel.hpp"

#include "../hsm_acked_binary_link.hpp"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace hsm::http
{
    class SessionTransport;
}

namespace hsm::collector::tables
{
    struct SessionTableChannelOptions
    {
        static constexpr const char* kSubprotocol = "hsm.tables.v1";

        /// How long a request (a registration, a block) waits for its ack.
        std::int64_t request_timeout_ms = 30000;
        /// Bounds one open: the upgrade, the hello and the probe's ack.
        std::int64_t open_timeout_ms = 15000;
        /// How long one put waits for a table connection before it answers "not delivered".
        std::int64_t connection_wait_ms = 15000;
        /// How long a refusal for good (404, 400) waits before it is asked again; a new session asks
        /// at once.
        std::int64_t refused_retry_ms = 5 * 60 * 1000;
        /// The pause before the next attempt; empty: the session's backoff with jitter.
        std::function<std::int64_t(int attempt)> backoff_ms;
        /// The wire JSON of one registration (AddOrUpdateSensorRequest with SensorType 11).
        std::function<std::string(const TableRegistration&)> registration_json;
        /// Where WriteReports writes: `error` true for the deduplicated error channel.
        std::function<void(bool error, const std::string& message)> log;
        /// For tests: see AckedLinkOptions::ack_abandoned_for_tests.
        std::function<bool(std::int64_t request_id)> ack_abandoned_for_tests;
    };

    class SessionTableChannel final : public ITableChannel
    {
    public:
        static constexpr std::size_t kDefaultFrameBytes = 4 * 1024 * 1024;
        static constexpr int kConnectionsPerPut = 3;
        static constexpr std::int64_t kMaxRetryAfterMs = 5 * 60 * 1000;
        static constexpr std::int64_t kStableConnectionMs = 60000;

        explicit SessionTableChannel(SessionTableChannelOptions options);
        ~SessionTableChannel() override;

        SessionTableChannel(const SessionTableChannel&) = delete;
        SessionTableChannel& operator=(const SessionTableChannel&) = delete;

        /// The session the run uses (a new transport object at every Start). Called before the run's
        /// threads exist; null detaches.
        void Attach(hsm::http::SessionTransport* session);

        // ITableChannel
        std::size_t MaxBlockBytes() const override;
        TableBlockLimits Limits() const override;
        TableRegistrationResult Register(const std::vector<TableRegistration>& registrations) override;
        TableBlockAck Put(const TableProducerId& producer, std::int64_t sequence, std::vector<std::uint8_t>& frame) override;
        void Cancel() override;
        void Reset() override;
        std::int64_t ServiceDelayMs() override;
        bool Service() override;
        void WriteReports() noexcept override;

        /// Closes the table connection with 1000, waiting up to `linger_ms` for the server's answer
        /// (0: the close frame is written once, nothing is waited for). For the owner, after the
        /// delivery thread was joined: the stop calls it before it closes the session.
        void Close(std::int64_t linger_ms) noexcept;

        // ---- Tests (any thread) ----
        /// Table connections opened so far (hello read, probe answered).
        std::int64_t ConnectionsOpened() const noexcept { return opened_.load(); }
        /// Upgrades attempted so far, opened or not.
        std::int64_t OpenAttempts() const noexcept { return attempts_.load(); }
        /// A table connection is open now.
        bool IsServing() const noexcept { return serving_.load(); }
        std::string DownReason() const;
        /// The report of a failed open, as TryOpen writes it (the dedup by kind is tested through it).
        void ReportOpenFailureForTests(const hsm::http::AckedLinkOpen& opened) { ReportOpenFailure(opened, "test-session"); }
        /// The answer the channel gives to an ack (the ack table is tested through it).
        TableBlockAck AnswerForTests(const hsm::http::AckedLinkAck& ack) { return Answer(ack); }

    private:
        struct EndInfo
        {
            hsm::http::AckedLinkEnd end;
            bool session_changed = false;
        };

        // Keeps the connection: serves an open one (a new session replaces it), opens one when it is
        // wanted and allowed. True when one is open on return.
        bool Maintain();
        bool TryOpen(std::uint64_t generation, const std::string& session_id);
        bool HelloCheck(const std::string& body, std::string& why);
        void HandleEnd(const hsm::http::AckedLinkEnd& end, bool session_changed);
        // Waits for an open connection until `deadline`; false when there is none by then.
        bool WaitForLink(std::chrono::steady_clock::time_point deadline);
        bool WaitFor(std::chrono::steady_clock::time_point until);
        TableBlockAck Answer(const hsm::http::AckedLinkAck& ack);
        TableBlockAck ReadOutcome(const hsm::http::AckedLinkAck& ack);
        std::int64_t Backoff();
        void SetDown(std::string reason);
        // Queued for WriteReports. A report is said once per kind until a connection opens: `kind`
        // names the failure without its words (which may carry elapsed times), the words of the first
        // one go into the line.
        void Report(const std::string& kind, std::string message, bool error = true);
        void Info(std::string message);
        void ReportOpenFailure(const hsm::http::AckedLinkOpen& opened, const std::string& session_id);
        std::string Url() const;

        const SessionTableChannelOptions options_;
        std::atomic<hsm::http::SessionTransport*> session_{ nullptr };
        std::atomic<bool> cancelled_{ false };

        // The link: replaced by Attach only (no thread then); Cancel reaches it under link_mutex_.
        mutable std::mutex link_mutex_;
        std::unique_ptr<hsm::http::AckedBinaryLink> link_;

        // The limits of the last hello: read by the tick thread and the application.
        mutable std::mutex limits_mutex_;
        std::size_t max_frame_bytes_ = kDefaultFrameBytes;
        TableBlockLimits limits_{};

        // For the owner's waits, woken by Cancel.
        std::mutex wait_mutex_;
        std::condition_variable wait_cv_;

        // Owned by the owner thread.
        bool wanted_ = false;
        std::uint64_t link_generation_ = 0;
        std::chrono::steady_clock::time_point serving_since_{};
        int attempt_ = 0;
        std::chrono::steady_clock::time_point next_attempt_at_{};
        bool lift_with_next_session_ = false;
        std::uint64_t backoff_generation_ = 0;
        long refused_status_ = 0;
        std::chrono::steady_clock::time_point refused_at_{};
        std::uint64_t refused_generation_ = 0;
        std::string refused_session_;
        std::uint64_t no_id_generation_ = 0;
        std::string hello_problem_;

        std::atomic<std::int64_t> opened_{ 0 };
        std::atomic<std::int64_t> attempts_{ 0 };
        std::atomic<bool> serving_{ false };

        mutable std::mutex state_mutex_;
        std::string down_reason_ = "the table connection is not open yet";
        std::string url_;

        std::mutex reports_mutex_;
        std::deque<std::pair<bool, std::string>> reports_;
        // The kinds reported since the last connection opened.
        std::set<std::string> reported_kinds_;
        static constexpr std::size_t kMaxReportedKinds = 64;
    };
} // namespace hsm::collector::tables
