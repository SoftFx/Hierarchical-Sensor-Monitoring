#pragma once

/// @file
/// @brief The table sensors of one native collector (#1848, ADR-0021 points 5-7). Internal; the managed
/// counterpart is HSMDataCollector.Tables.TablePipeline (aicontext/features/collector/table-sensors).
///
/// The pipeline exists only once the first table sensor is created: a collector without table sensors
/// has no pipeline, no zstd context, no tick, no thread and no log line of this feature.
///
/// Threads. Rows are added by the application on any thread (one mutex of their sensor, never I/O).
/// The tick is a ScheduledTask of its own on the collector's clock: it reports losses, asks for the
/// registration of Pending sensors, packs the rows of every Registered sensor into blocks (zstd) and
/// wakes delivery. The delivery thread sends registrations and puts the head of the backlog, one block
/// in flight, stop and wait. Both threads exist from Start to Stop of a collector that has a table
/// sensor (the delivery thread only when a channel exists), and Stop joins them: when Stop returns, no
/// thread of the run exists, so nothing of one run reaches the next and no run object is needed.
///
/// Lifetime. The collector owns the pipeline; the pipeline owns its sensors and threads. A sensor handle
/// of the C ABI holds the sensor (shared), and the sensor holds only TableShared (flags, the buffer
/// bound, the loss ledger): never the pipeline nor the collector, so an application thread can never
/// become the collector's last owner, and after the collector is destroyed its calls are refused.

#include "hsm_collector/hsm_collector.h"

#include "../hsm_scheduled_task.hpp"
#include "hsm_table_channel.hpp"
#include "hsm_table_row_buffer.hpp"
#include "hsm_table_schema.hpp"

#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace hsm::collector::tables
{
#if defined(HSM_COLLECTOR_TABLES)
    class TableBlockWriter;
#endif

    /// Why rows were lost; each reason is reported in words of its own.
    enum class TableDropReason : int
    {
        None = 0,
        NotRunning,
        BufferFull,
        SensorRefused,
        SensorUnavailable,
        BacklogFull,
        RefusedByServer,
        NotDeliveredAtStop,
        RowTooLarge,
        PackingFailed,
        BufferGrowthFailed,
        Count,
    };

    /// The path of a table sensor as the server stores it: segments split on '/', white space (the .NET
    /// char.IsWhiteSpace set) trimmed around each, empty segments dropped.
    std::string NormalizeTablePath(const std::string& path);

    /// False, with `error`, for a normalized path the server would refuse whatever is retried: empty,
    /// with a backslash or a tab, or longer in UTF-8 than a block may name a table.
    bool CheckTablePath(const std::string& normalized, const std::string& given, std::int32_t max_bytes, std::string& error);

    /// Counters of lost rows by reason. Lock-free; reported once per tick and at the end of a stop.
    class TableLedger
    {
    public:
        TableLedger() noexcept;

        void Add(std::int64_t rows, TableDropReason reason) noexcept;

        /// The reasons with rows lost since the last call, each once.
        std::vector<TableDropReason> TakeReasons();
        /// Forgets the reasons not yet worded (the ledger closes). Allocates nothing.
        void ClearReasons() noexcept;

        /// Takes the unreported rows to hand them to the overflow sensor; GiveBack when it refuses them.
        std::int64_t TakeUnreported() noexcept;
        void GiveBack(std::int64_t rows) noexcept;

        /// For tests: every row counted under `reason` since the pipeline was made.
        std::int64_t Total(TableDropReason reason) const noexcept;

    private:
        static constexpr int kReasons = static_cast<int>(TableDropReason::Count);
        std::array<std::atomic<std::int64_t>, kReasons> total_;
        std::array<std::atomic<std::int64_t>, kReasons> unworded_;
        std::atomic<std::int64_t> unreported_{ 0 };
    };

    /// What every sensor of a pipeline shares with it: no thread, no reference to the collector.
    struct TableShared
    {
        explicit TableShared(std::int64_t max_buffer_bytes) noexcept
            : max_buffer_bytes(max_buffer_bytes)
        {
        }

        /// A run takes rows: from the pipeline's Start until its stop begins.
        std::atomic<bool> accepting{ false };
        /// Drops are counted in the ledger: from Start until the stop line of the collector's stop.
        std::atomic<bool> ledger_open{ false };
        /// The collector was disposed: every call is refused.
        std::atomic<bool> disposed{ false };
        /// Bytes of rows waiting for a tick, against max_buffer_bytes. Never reset: every reservation
        /// is given back by its owner, so a writer preempted across a Stop and a Start cannot corrupt it.
        std::atomic<std::int64_t> reserved{ 0 };
        const std::int64_t max_buffer_bytes;
        TableLedger ledger;

        /// Reserves `bytes` unless that would pass the bound; never overshoots (compare and swap).
        bool TryReserve(std::int64_t bytes) noexcept;
        void Release(std::int64_t bytes) noexcept;

        /// For tests: runs on the writer's thread between the reservation and the append.
        std::function<void()> before_append;
    };

    /// One table sensor: schema, state, counters and the two row buffers swapped by a tick.
    class TableSensor
    {
    public:
        TableSensor(std::shared_ptr<TableShared> shared, std::string path, std::vector<TableRegistrationColumn> columns,
                    TableSchema schema, std::string description, std::int64_t keep_history_ms);

        TableSensor(const TableSensor&) = delete;
        TableSensor& operator=(const TableSensor&) = delete;

        const std::string& Path() const noexcept { return path_; }
        const TableSchema& Schema() const noexcept { return schema_; }
        std::size_t ColumnCount() const noexcept { return columns_.size(); }
        TableColumnType ColumnType(std::size_t column) const noexcept { return columns_[column].type; }
        /// The bytes one buffered row counts against the buffer bound: 8 per column, the time included.
        std::int64_t RowBytes() const noexcept { return row_bytes_; }

        /// One add call, all or nothing. The pointers and the cell count were checked by the caller; a
        /// row count that cannot be a real array is refused here (HSM_RESULT_INVALID_ARGUMENT, counted
        /// nowhere). Never throws.
        hsm_result_t Add(const std::int64_t* times_unix_ns, const hsm_table_cell_t* cells, const std::uint8_t* nulls, std::size_t rows) noexcept;

        hsm_table_sensor_state_t State() const noexcept { return static_cast<hsm_table_sensor_state_t>(state_.load(std::memory_order_acquire)); }
        std::string Reason() const;
        std::uint64_t RowsAccepted() const noexcept { return accepted_.load(std::memory_order_relaxed); }
        std::uint64_t RowsDropped() const noexcept { return dropped_.load(std::memory_order_relaxed); }

        /// Counts lost rows on the sensor, and in the ledger while it is open.
        void Drop(std::int64_t rows, TableDropReason reason) noexcept;

        /// Moves the state from `from` to `to`; false when it was not `from`.
        bool TryMove(hsm_table_sensor_state_t from, hsm_table_sensor_state_t to, const std::string& reason = std::string());
        void SetState(hsm_table_sensor_state_t to, const std::string& reason);

        /// Takes the rows added since the last take; null when there are none. Give the buffer back.
        std::unique_ptr<TableRowBuffer> TakeRows();
        void ReturnRows(std::unique_ptr<TableRowBuffer> buffer) noexcept;

        /// Drops every buffered row under `reason`, gives their room back, returns how many there were.
        /// Allocates nothing.
        std::int64_t DiscardRows(TableDropReason reason) noexcept;

        /// True the first time only: a table the connection's limits can never take is reported once.
        bool FirstLimitReport() noexcept { return !limit_reported_.exchange(true, std::memory_order_acq_rel); }

        TableRegistration BuildRegistration() const;

        const std::shared_ptr<TableShared>& Shared() const noexcept { return shared_; }

        /// For tests: the rows the arrays of both buffers hold without growing.
        std::size_t RetainedRowCapacity() const;

        /// For tests: runs before a buffer of this sensor allocates the arrays of a growth.
        void SetBeforeGrowthForTests(std::function<void()> hook);

    private:
        std::shared_ptr<TableShared> shared_;
        const std::string path_;
        const std::vector<TableRegistrationColumn> columns_;
        const TableSchema schema_;
        const std::string description_;
        const std::int64_t keep_history_ms_;
        const std::int64_t row_bytes_;

        mutable std::mutex mutex_;
        std::unique_ptr<TableRowBuffer> active_;
        std::unique_ptr<TableRowBuffer> spare_;
        std::function<void()> before_growth_;
        std::string reason_;
        std::atomic<int> state_{ HSM_TABLE_SENSOR_PENDING };
        std::atomic<std::uint64_t> accepted_{ 0 };
        std::atomic<std::uint64_t> dropped_{ 0 };
        std::atomic<bool> limit_reported_{ false };
    };

    /// A packed block waiting for its final answer; its tables in directory order.
    struct PendingBlock
    {
        struct Table
        {
            std::shared_ptr<TableSensor> sensor;
            std::int64_t rows;
        };

        std::int64_t sequence = 0;
        /// kTableFrameHeaderBytes reserved, then the block.
        std::vector<std::uint8_t> frame;
        std::vector<Table> tables;
        std::int64_t rows = 0;

        std::int64_t BlockBytes() const noexcept { return static_cast<std::int64_t>(frame.size() - kTableFrameHeaderBytes); }
        void DropRows(TableDropReason reason) const noexcept;
    };

    /// Packed blocks in sequence order, bounded in bytes: past the bound the oldest go, never the one in
    /// flight, and their sequences are left as a gap.
    class TableBacklog
    {
    public:
        explicit TableBacklog(std::int64_t max_bytes) noexcept
            : max_bytes_(max_bytes)
        {
        }

        /// Appends a block; the blocks the bound drops are counted as BacklogFull at once. Returns how
        /// many blocks went. Throws only when the block cannot be appended, and then nothing changed.
        std::size_t Append(std::shared_ptr<PendingBlock> block);
        /// The head, now in flight; null when empty.
        std::shared_ptr<PendingBlock> BeginHead();
        /// A put that got no final answer: the block stays at the head, no longer in flight.
        void EndFlight(const std::shared_ptr<PendingBlock>& block) noexcept;
        /// A final answer: removes the block (false when it is no longer there) and ends its flight.
        bool Settle(const std::shared_ptr<PendingBlock>& block);
        /// Drops every block under `reason`, for a stop: the backlog is empty after. Allocates nothing.
        void DropAll(TableDropReason reason) noexcept;

        std::int64_t Bytes() const;
        std::size_t Blocks() const;
        std::vector<std::int64_t> Sequences() const;

    private:
        const std::int64_t max_bytes_;
        mutable std::mutex mutex_;
        std::deque<std::shared_ptr<PendingBlock>> blocks_;
        const PendingBlock* in_flight_ = nullptr;
        std::int64_t bytes_ = 0;
    };

    /// What the pipeline needs from its collector. Called with no lock of the pipeline held.
    class TablePipelineHost
    {
    public:
        virtual ~TablePipelineHost() = default;
        /// The collector's monotonic clock (the tests' manual clock drives the tick through it).
        virtual std::int64_t TableSteadyNowMs() = 0;
        /// The deduplicated error channel: the logger and ".module/Collector errors".
        virtual void ReportTableError(const std::string& message) = 0;
        /// Hands lost rows to the Queue overflow sensor; false when it takes none now (not registered,
        /// or it has published the last bar of the run): the rows then stay in the ledger.
        virtual bool TryReportDroppedTableRows(std::int64_t rows) = 0;
    };

    /// A failure whose report allocates nothing when it happens: a flag, worded when reports are written.
    enum class TableFault : int
    {
        PackFailed = 0,
        TickFailed,
        DeliveryFailed,
        DrainFailed,
        StartFailed,
        StopFailed,
        Count,
    };

    /// Why a table can never be in a block under `limits` (its identifier or its column count), whatever
    /// the split; empty when it can.
    std::string TableLimitProblem(const std::string& path, std::size_t value_columns, const TableBlockLimits& limits);

    struct TablePipelineOptions
    {
        std::int64_t tick_period_ms = 10000;
        std::int64_t max_buffer_bytes = 64LL * 1024 * 1024;
        std::int64_t max_backlog_bytes = 64LL * 1024 * 1024;
        /// Tests: no tick task and no delivery thread; RunTickNow does a tick on the caller's thread,
        /// with its registration and its delivery, and a stop drains on the caller's thread.
        bool manual_tick = false;
    };

    class TablePipeline
    {
    public:
        TablePipeline(TablePipelineHost& host, const TablePipelineOptions& options);
        ~TablePipeline();

        TablePipeline(const TablePipeline&) = delete;
        TablePipeline& operator=(const TablePipeline&) = delete;

        /// Checks the value columns of a table sensor and builds its schema (the time first). False with
        /// `error` for a mistake of the call.
        static bool BuildSchema(const std::vector<TableRegistrationColumn>& columns, TableSchema& schema, std::string& error);

        /// Creates a sensor, or returns the one created before under the same path with the same
        /// columns. False with `error` for a mistake of the call. A stopped pipeline decides by what the
        /// collector has now: `channel` (the sensor is Unavailable when the channel's limits can never
        /// take its table), else Unavailable with `unavailable_reason`; a running pipeline decides by
        /// its own run instead.
        bool CreateSensor(const std::string& path, const std::vector<TableRegistrationColumn>& columns,
                          const std::string& description, std::int64_t keep_history_ms,
                          const std::shared_ptr<ITableChannel>& channel, const std::string& unavailable_reason,
                          std::shared_ptr<TableSensor>& out, std::string& error);

        /// Starts a run: a new producer id, sequence 0, every sensor registered again (Registered and
        /// Unavailable go to Pending when `channel` exists and its limits can take the table; every
        /// sensor but a Refused one becomes Unavailable with `unavailable_reason` when it does not), the
        /// tick and the delivery thread. Queues its reports: the caller flushes them once it holds no
        /// lock (a host logger may call back into the collector). A start whose threads cannot be made
        /// is rolled back: no thread of the run is left, rows are refused, and the sensors are
        /// Unavailable for this run (tried again at the next Start).
        void Start(std::shared_ptr<ITableChannel> channel, const std::string& unavailable_reason) noexcept;
        /// The first half of a stop, before the collector's final Queue overflow report: rows are
        /// refused from now on, the tick is joined, every buffer is taken (each sensor's mutex once) and
        /// packed, and the losses so far are reported. The stop budget of `budget_ms` starts here and
        /// covers the final pack and the delivery (the managed drain budget): the rows not packed by its
        /// deadline are dropped and counted as not delivered at stop.
        void BeginStop(std::int64_t budget_ms) noexcept;
        /// Delivery drains the backlog until the stop budget's deadline, beside the collector's value
        /// drain.
        void StartDrain() noexcept;
        /// Waits for the drain until its deadline, cancels and joins the delivery thread, drops and
        /// counts what is left, reports the losses. When it returns no thread of the run exists.
        void FinishDrain() noexcept;
        /// The rows lost in the run that are in no report of the Queue overflow sensor; closes the
        /// ledger (a row refused after it is counted on its sensor only).
        std::int64_t CloseLedger() noexcept;
        /// Ends everything; every later call of a sensor is refused. Idempotent.
        void Dispose() noexcept;

        bool Running() const;
        /// Nudges the tick to read the clock again (a manual clock moved).
        void WakeTick();
        /// Writes the queued reports. Call with no lock of the collector or of the pipeline held.
        void FlushReports() noexcept;
        /// Records a failure to report, allocating nothing.
        void Fault(TableFault fault) noexcept;

        // ---- Tests ----
        /// One tick on the caller's thread; with manual_tick its registration and delivery too.
        void RunTickNow();
        bool TickAlive() const;
        bool DeliveryThreadAlive() const;
        std::int64_t RunCount() const noexcept { return run_count_.load(); }
        std::int64_t ReservedBytes() const noexcept { return shared_->reserved.load(); }
        std::int64_t BacklogBytes() const { return backlog_.Bytes(); }
        std::size_t BacklogBlocks() const { return backlog_.Blocks(); }
        TableProducerId Producer() const;
        const TableLedger& Ledger() const noexcept { return shared_->ledger; }
        TableShared& Shared() noexcept { return *shared_; }
        bool HasWriter() const;
        /// For tests: runs after a block got its answer and before the answer is applied.
        std::function<void()> on_ack_for_tests;
        /// For tests: runs before every allocation of a pack (a test makes it throw std::bad_alloc).
        std::function<void()> on_pack_allocation_for_tests;
        /// For tests: runs before every compression pass of a pack.
        std::function<void()> on_pack_pass_for_tests;
        /// For tests: the stop budget is spent from now on (until the next Start).
        void ExpireStopBudgetForTests() noexcept { budget_expired_for_tests_.store(true); }
        /// For tests: the clock never spends the stop budget; only ExpireStopBudgetForTests does.
        void IgnoreStopClockForTests() noexcept { stop_clock_ignored_for_tests_.store(true); }
        /// For tests: runs before a thread of a run is made (a test makes it throw std::system_error).
        std::function<void()> before_thread_start_for_tests;
        /// For tests: runs in the stop's FinishDrain before what is left is counted (a test throws).
        std::function<void()> in_finish_drain_for_tests;

    private:
        struct Taken;
        struct PackPass;

        void Tick() noexcept;
        void ReportLosses() noexcept;
        void Note(std::string message) noexcept;
        std::vector<std::shared_ptr<TableSensor>> SnapshotSensors() const;
        void RegisterPending();
        void Pack(bool final_pack, std::chrono::steady_clock::time_point deadline = std::chrono::steady_clock::time_point::max()) noexcept;
        bool StopBudgetSpent(std::chrono::steady_clock::time_point deadline) const noexcept;
        void PackTaken(std::vector<Taken>& taken, PackPass& pass);
        void PackAllocation();
        void StartThreads();
        void DiscardAllRows(TableDropReason reason) noexcept;
        void Deliver(bool at_stop, std::chrono::steady_clock::time_point deadline);
        void Settle(const std::shared_ptr<PendingBlock>& block, const TableBlockAck& ack);
        void ApplyRefusals(const PendingBlock& block, const TableBlockAck& ack, bool count_rows);
        void RequestDelivery(bool registration, bool blocks);
        void DeliveryLoop() noexcept;
        void StopThread(std::thread& delivery, const std::shared_ptr<ITableChannel>& channel, bool cancel);
        std::string Describe(TableDropReason reason) const;

#if defined(HSM_COLLECTOR_TABLES)
        struct Piece
        {
            TableSensor* sensor;
            const TableRowBuffer* buffer;
            std::shared_ptr<TableSensor> owner;
            std::size_t start;
            std::size_t count;
            /// The rows of its take that are in a queued block or counted as lost.
            std::int64_t* settled;
        };
        void EncodeAndQueue(const std::vector<Piece>& group, std::size_t max_block, PackPass& pass);
        int Encode(const std::vector<Piece>& group, std::size_t max_block, std::vector<std::uint8_t>& frame, const char*& error);
        void Enqueue(const std::vector<Piece>& group, std::vector<std::uint8_t> frame);
        std::unique_ptr<TableBlockWriter> writer_;
        std::vector<std::uint8_t> null_map_;
#endif

        TablePipelineHost& host_;
        const TablePipelineOptions options_;
        const std::shared_ptr<TableShared> shared_;
        TableBacklog backlog_;

        mutable std::mutex sensors_mutex_;
        std::map<std::string, std::shared_ptr<TableSensor>> sensors_;

        // Start, the stop's steps and Dispose.
        mutable std::mutex lifecycle_mutex_;
        bool running_ = false;
        bool disposed_ = false;
        std::shared_ptr<ITableChannel> channel_;
        std::string channel_reason_;
        std::chrono::steady_clock::time_point drain_deadline_{};
        std::atomic<bool> budget_expired_for_tests_{ false };
        std::atomic<bool> stop_clock_ignored_for_tests_{ false };
        std::unique_ptr<detail::ScheduledTask> tick_;
        std::atomic<std::int64_t> last_tick_ms_{ 0 };
        std::atomic<std::int64_t> run_count_{ 0 };

        // The tick's pack and the stop's final pack; the writer, the producer's sequence.
        mutable std::mutex pack_mutex_;
        TableProducerId producer_{};
        std::int64_t next_sequence_ = 0;

        // One registration request at a time; one deliverer at a time.
        std::mutex register_mutex_;
        std::mutex deliver_mutex_;
        std::atomic<std::int64_t> retry_not_before_ms_{ 0 };

        // The delivery thread and what it is asked to do.
        std::thread delivery_thread_;
        std::mutex delivery_mutex_;
        std::condition_variable delivery_cv_;
        std::condition_variable drained_cv_;
        bool register_due_ = false;
        bool deliver_due_ = false;
        bool drain_requested_ = false;
        bool drain_done_ = false;
        std::atomic<bool> stop_delivery_{ false };

        std::mutex reports_mutex_;
        std::vector<std::string> reports_;
        std::array<std::atomic<bool>, static_cast<std::size_t>(TableFault::Count)> faults_{};
    };
} // namespace hsm::collector::tables
