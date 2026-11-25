#include "hsm_table_pipeline.hpp"

#if defined(HSM_COLLECTOR_TABLES)
#include "hsm_table_block_writer.hpp"
#endif

#include <algorithm>
#include <cstring>
#include <exception>
#include <limits>
#include <new>
#include <random>
#include <utility>

namespace hsm::collector::tables
{
    namespace
    {
        constexpr std::int64_t kMaxRetryAfterMs = 5 * 60 * 1000;

        // The .NET char.IsWhiteSpace set (what String.Trim removes), as UTF-8: the ASCII white space,
        // U+0085, and the Unicode separators Zs, Zl and Zp.
        std::size_t WhiteSpaceAt(const std::string& text, std::size_t at)
        {
            const auto byte = [&](std::size_t i) { return i < text.size() ? static_cast<unsigned char>(text[i]) : 0u; };
            const unsigned b0 = byte(at);
            if (b0 == 0x20 || (b0 >= 0x09 && b0 <= 0x0D))
                return 1;
            if (b0 == 0xC2 && (byte(at + 1) == 0x85 || byte(at + 1) == 0xA0))
                return 2;
            if (b0 == 0xE1 && byte(at + 1) == 0x9A && byte(at + 2) == 0x80)
                return 3; // U+1680
            if (b0 == 0xE2 && byte(at + 1) == 0x80)
            {
                const unsigned b2 = byte(at + 2);
                if ((b2 >= 0x80 && b2 <= 0x8A) || b2 == 0xA8 || b2 == 0xA9 || b2 == 0xAF)
                    return 3; // U+2000..U+200A, U+2028, U+2029, U+202F
            }
            if (b0 == 0xE2 && byte(at + 1) == 0x81 && byte(at + 2) == 0x9F)
                return 3; // U+205F
            if (b0 == 0xE3 && byte(at + 1) == 0x80 && byte(at + 2) == 0x80)
                return 3; // U+3000
            return 0;
        }

        // The length of the white space that ends text[0, end), in bytes.
        std::size_t WhiteSpaceBefore(const std::string& text, std::size_t begin, std::size_t end)
        {
            for (std::size_t length = 1; length <= 3 && length <= end - begin; ++length)
                if (WhiteSpaceAt(text, end - length) == length)
                    return length;
            return 0;
        }

        TableProducerId NewProducerId()
        {
            TableProducerId id{};
            try
            {
                std::random_device device;
                for (std::size_t i = 0; i < id.size(); i += 4)
                {
                    const auto value = static_cast<std::uint32_t>(device());
                    for (std::size_t k = 0; k < 4; ++k)
                        id[i + k] = static_cast<std::uint8_t>(value >> (8 * k));
                }
                return id;
            }
            catch (...)
            {
                // No random device: a seed of the clock and an address is still unique enough for one
                // host's runs (the producer only separates runs of the same collector).
            }
            std::mt19937_64 generator(static_cast<std::uint64_t>(std::chrono::steady_clock::now().time_since_epoch().count()) ^
                                      reinterpret_cast<std::uintptr_t>(&id));
            for (std::size_t i = 0; i < id.size(); i += 8)
            {
                const std::uint64_t value = generator();
                for (std::size_t k = 0; k < 8; ++k)
                    id[i + k] = static_cast<std::uint8_t>(value >> (8 * k));
            }
            return id;
        }
    } // namespace

    // ---- Paths ---------------------------------------------------------------------------------------

    std::string NormalizeTablePath(const std::string& path)
    {
        std::string result;
        std::size_t start = 0;
        while (start <= path.size())
        {
            std::size_t end = path.find('/', start);
            if (end == std::string::npos)
                end = path.size();

            std::size_t first = start;
            while (first < end)
            {
                const std::size_t length = WhiteSpaceAt(path, first);
                if (length == 0 || first + length > end)
                    break;
                first += length;
            }
            std::size_t last = end;
            while (last > first)
            {
                const std::size_t length = WhiteSpaceBefore(path, first, last);
                if (length == 0)
                    break;
                last -= length;
            }

            if (last > first)
            {
                if (!result.empty())
                    result += '/';
                result.append(path, first, last - first);
            }
            start = end + 1;
        }
        return result;
    }

    bool CheckTablePath(const std::string& normalized, const std::string& given, std::int32_t max_bytes, std::string& error)
    {
        if (normalized.empty())
        {
            error = "A table sensor needs a path; '" + given + "' has no segment.";
            return false;
        }
        if (normalized.find('\\') != std::string::npos || normalized.find('\t') != std::string::npos)
        {
            error = "The path '" + given + "' contains a backslash or a tab, which the server refuses.";
            return false;
        }
        if (normalized.size() > static_cast<std::size_t>(max_bytes))
        {
            error = "A block names a table sensor by its path in UTF-8, at most " + std::to_string(max_bytes) +
                    " bytes; '" + normalized + "' has " + std::to_string(normalized.size()) + ".";
            return false;
        }
        return true;
    }

    // ---- The ledger and the shared block -------------------------------------------------------------

    TableLedger::TableLedger() noexcept
    {
        for (int i = 0; i < kReasons; ++i)
        {
            total_[i].store(0);
            unworded_[i].store(0);
        }
    }

    void TableLedger::Add(std::int64_t rows, TableDropReason reason) noexcept
    {
        if (rows <= 0)
            return;
        const int index = static_cast<int>(reason);
        total_[index].fetch_add(rows, std::memory_order_relaxed);
        unworded_[index].fetch_add(rows, std::memory_order_relaxed);
        unreported_.fetch_add(rows, std::memory_order_relaxed);
    }

    std::vector<TableDropReason> TableLedger::TakeReasons()
    {
        std::vector<TableDropReason> reasons;
        for (int i = 1; i < kReasons; ++i)
            if (unworded_[i].exchange(0, std::memory_order_relaxed) > 0)
                reasons.push_back(static_cast<TableDropReason>(i));
        return reasons;
    }

    std::int64_t TableLedger::TakeUnreported() noexcept
    {
        return unreported_.exchange(0, std::memory_order_relaxed);
    }

    void TableLedger::GiveBack(std::int64_t rows) noexcept
    {
        if (rows > 0)
            unreported_.fetch_add(rows, std::memory_order_relaxed);
    }

    std::int64_t TableLedger::Total(TableDropReason reason) const noexcept
    {
        return total_[static_cast<int>(reason)].load(std::memory_order_relaxed);
    }

    bool TableShared::TryReserve(std::int64_t bytes) noexcept
    {
        // A request larger than the bound is refused before the counter is touched.
        if (bytes > max_buffer_bytes)
            return false;
        std::int64_t current = reserved.load(std::memory_order_relaxed);
        do
        {
            if (current > max_buffer_bytes - bytes)
                return false;
        } while (!reserved.compare_exchange_weak(current, current + bytes, std::memory_order_acq_rel, std::memory_order_relaxed));
        return true;
    }

    void TableShared::Release(std::int64_t bytes) noexcept
    {
        if (bytes > 0)
            reserved.fetch_sub(bytes, std::memory_order_acq_rel);
    }

    // ---- The sensor ----------------------------------------------------------------------------------

    TableSensor::TableSensor(std::shared_ptr<TableShared> shared, std::string path, std::vector<TableRegistrationColumn> columns,
                             TableSchema schema, std::string description, std::int64_t keep_history_ms)
        : shared_(std::move(shared)),
          path_(std::move(path)),
          columns_(std::move(columns)),
          schema_(std::move(schema)),
          description_(std::move(description)),
          keep_history_ms_(keep_history_ms),
          row_bytes_(static_cast<std::int64_t>(8 * (columns_.size() + 1))),
          active_(std::make_unique<TableRowBuffer>(columns_.size())),
          spare_(std::make_unique<TableRowBuffer>(columns_.size()))
    {
    }

    hsm_result_t TableSensor::Add(const std::int64_t* times_unix_ns, const hsm_table_cell_t* cells, const std::uint8_t* nulls, std::size_t rows) noexcept
    {
        const auto count = static_cast<std::int64_t>(rows);
        if (shared_->disposed.load(std::memory_order_acquire))
        {
            // The collector is gone: on the sensor only, its ledger is closed for good.
            dropped_.fetch_add(rows, std::memory_order_relaxed);
            return HSM_RESULT_INVALID_STATE;
        }

        const int state = state_.load(std::memory_order_acquire);
        if (state == HSM_TABLE_SENSOR_REFUSED || state == HSM_TABLE_SENSOR_UNAVAILABLE)
        {
            Drop(count, state == HSM_TABLE_SENSOR_REFUSED ? TableDropReason::SensorRefused : TableDropReason::SensorUnavailable);
            return HSM_RESULT_INVALID_STATE;
        }
        if (!shared_->accepting.load(std::memory_order_acquire))
        {
            Drop(count, TableDropReason::NotRunning);
            return HSM_RESULT_INVALID_STATE;
        }

        if (rows > static_cast<std::size_t>((std::numeric_limits<std::int64_t>::max)() / row_bytes_))
        {
            Drop(count, TableDropReason::BufferFull);
            return HSM_RESULT_LIMIT_EXCEEDED;
        }
        const std::int64_t bytes = count * row_bytes_;
        if (!shared_->TryReserve(bytes))
        {
            Drop(count, TableDropReason::BufferFull);
            return HSM_RESULT_LIMIT_EXCEEDED;
        }

        if (shared_->before_append)
        {
            try
            {
                shared_->before_append();
            }
            catch (...)
            {
            }
        }

        TableDropReason reason = TableDropReason::None;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            // Checked again under the mutex the stop's final take and every state change take: a row
            // reserved while the sensor was writable is not appended once it is not.
            const int now = state_.load(std::memory_order_acquire);
            if (now == HSM_TABLE_SENSOR_REFUSED)
                reason = TableDropReason::SensorRefused;
            else if (now == HSM_TABLE_SENSOR_UNAVAILABLE)
                reason = TableDropReason::SensorUnavailable;
            else if (!shared_->accepting.load(std::memory_order_acquire))
                reason = TableDropReason::NotRunning;
            else
            {
                try
                {
                    active_->Append(times_unix_ns, cells, nulls, rows);
                }
                catch (...)
                {
                    // Out of memory while the buffer grew: it is as it was before the call.
                    reason = TableDropReason::BufferGrowthFailed;
                }
            }
        }

        if (reason != TableDropReason::None)
        {
            shared_->Release(bytes);
            Drop(count, reason);
            return reason == TableDropReason::BufferGrowthFailed ? HSM_RESULT_LIMIT_EXCEEDED : HSM_RESULT_INVALID_STATE;
        }

        accepted_.fetch_add(rows, std::memory_order_relaxed);
        return HSM_RESULT_OK;
    }

    std::string TableSensor::Reason() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return reason_;
    }

    void TableSensor::Drop(std::int64_t rows, TableDropReason reason) noexcept
    {
        if (rows <= 0)
            return;
        dropped_.fetch_add(static_cast<std::uint64_t>(rows), std::memory_order_relaxed);
        if (shared_->ledger_open.load(std::memory_order_acquire))
            shared_->ledger.Add(rows, reason);
    }

    bool TableSensor::TryMove(hsm_table_sensor_state_t from, hsm_table_sensor_state_t to, const std::string& reason)
    {
        std::lock_guard<std::mutex> guard(mutex_);
        if (state_.load(std::memory_order_relaxed) != from)
            return false;
        reason_ = reason;
        state_.store(to, std::memory_order_release);
        return true;
    }

    void TableSensor::SetState(hsm_table_sensor_state_t to, const std::string& reason)
    {
        std::lock_guard<std::mutex> guard(mutex_);
        reason_ = reason;
        state_.store(to, std::memory_order_release);
    }

    std::unique_ptr<TableRowBuffer> TableSensor::TakeRows()
    {
        // Allocated before the lock, used only when the spare is away (a pack holds it).
        std::unique_ptr<TableRowBuffer> fresh;
        std::function<void()> hook;
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (active_->Count() == 0)
                return nullptr;
            hook = before_growth_;
            if (spare_)
            {
                std::unique_ptr<TableRowBuffer> taken = std::move(active_);
                active_ = std::move(spare_);
                return taken;
            }
        }
        fresh = std::make_unique<TableRowBuffer>(columns_.size());
        fresh->before_growth = std::move(hook);
        std::lock_guard<std::mutex> guard(mutex_);
        if (active_->Count() == 0)
            return nullptr;
        std::unique_ptr<TableRowBuffer> taken = std::move(active_);
        active_ = std::move(fresh);
        return taken;
    }

    void TableSensor::ReturnRows(std::unique_ptr<TableRowBuffer> buffer) noexcept
    {
        if (!buffer)
            return;
        buffer->Clear();
        std::lock_guard<std::mutex> guard(mutex_);
        if (!spare_)
            spare_ = std::move(buffer);
    }

    std::int64_t TableSensor::DiscardRows(TableDropReason reason)
    {
        std::unique_ptr<TableRowBuffer> taken = TakeRows();
        if (!taken)
            return 0;
        const auto rows = static_cast<std::int64_t>(taken->Count());
        ReturnRows(std::move(taken));
        shared_->Release(rows * row_bytes_);
        Drop(rows, reason);
        return rows;
    }

    TableRegistration TableSensor::BuildRegistration() const
    {
        TableRegistration registration;
        registration.path = path_;
        registration.columns.reserve(columns_.size() + 1);
        registration.columns.push_back({ "time", TableColumnType::Time });
        registration.columns.insert(registration.columns.end(), columns_.begin(), columns_.end());
        registration.description = description_;
        registration.keep_history_ms = keep_history_ms_;
        return registration;
    }

    std::size_t TableSensor::RetainedRowCapacity() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return (active_ ? active_->Capacity() : 0) + (spare_ ? spare_->Capacity() : 0);
    }

    void TableSensor::SetBeforeGrowthForTests(std::function<void()> hook)
    {
        std::lock_guard<std::mutex> guard(mutex_);
        before_growth_ = hook;
        if (active_)
            active_->before_growth = hook;
        if (spare_)
            spare_->before_growth = hook;
    }

    // ---- Blocks and the backlog ----------------------------------------------------------------------

    void PendingBlock::DropRows(TableDropReason reason) const noexcept
    {
        for (const Table& table : tables)
            table.sensor->Drop(table.rows, reason);
    }

    std::vector<std::shared_ptr<PendingBlock>> TableBacklog::Append(std::shared_ptr<PendingBlock> block)
    {
        std::vector<std::shared_ptr<PendingBlock>> dropped;
        std::lock_guard<std::mutex> guard(mutex_);
        bytes_ += block->BlockBytes();
        blocks_.push_back(std::move(block));

        // The oldest blocks go first, never the one being sent.
        for (auto it = blocks_.begin(); bytes_ > max_bytes_ && it != blocks_.end();)
        {
            if (it->get() == in_flight_)
            {
                ++it;
                continue;
            }
            bytes_ -= (*it)->BlockBytes();
            dropped.push_back(std::move(*it));
            it = blocks_.erase(it);
        }
        return dropped;
    }

    std::shared_ptr<PendingBlock> TableBacklog::BeginHead()
    {
        std::lock_guard<std::mutex> guard(mutex_);
        if (blocks_.empty())
        {
            in_flight_ = nullptr;
            return nullptr;
        }
        in_flight_ = blocks_.front().get();
        return blocks_.front();
    }

    void TableBacklog::EndFlight(const std::shared_ptr<PendingBlock>& block) noexcept
    {
        std::lock_guard<std::mutex> guard(mutex_);
        if (in_flight_ == block.get())
            in_flight_ = nullptr;
    }

    bool TableBacklog::Settle(const std::shared_ptr<PendingBlock>& block)
    {
        std::lock_guard<std::mutex> guard(mutex_);
        if (in_flight_ == block.get())
            in_flight_ = nullptr;
        const auto it = std::find(blocks_.begin(), blocks_.end(), block);
        if (it == blocks_.end())
            return false;
        bytes_ -= block->BlockBytes();
        blocks_.erase(it);
        return true;
    }

    std::vector<std::shared_ptr<PendingBlock>> TableBacklog::TakeAll()
    {
        std::lock_guard<std::mutex> guard(mutex_);
        std::vector<std::shared_ptr<PendingBlock>> all(std::make_move_iterator(blocks_.begin()), std::make_move_iterator(blocks_.end()));
        blocks_.clear();
        in_flight_ = nullptr;
        bytes_ = 0;
        return all;
    }

    std::int64_t TableBacklog::Bytes() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return bytes_;
    }

    std::size_t TableBacklog::Blocks() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        return blocks_.size();
    }

    std::vector<std::int64_t> TableBacklog::Sequences() const
    {
        std::lock_guard<std::mutex> guard(mutex_);
        std::vector<std::int64_t> sequences;
        for (const auto& block : blocks_)
            sequences.push_back(block->sequence);
        return sequences;
    }

    // ---- The pipeline --------------------------------------------------------------------------------

    TablePipeline::TablePipeline(TablePipelineHost& host, const TablePipelineOptions& options)
        : host_(host), options_(options), shared_(std::make_shared<TableShared>(options.max_buffer_bytes)), backlog_(options.max_backlog_bytes)
    {
    }

    TablePipeline::~TablePipeline()
    {
        Dispose();
    }

    void TablePipeline::Note(std::string message)
    {
        try
        {
            std::lock_guard<std::mutex> guard(reports_mutex_);
            reports_.push_back(std::move(message));
        }
        catch (...)
        {
            // A report that cannot be queued is lost; the counters still hold the rows.
        }
    }

    void TablePipeline::FlushReports()
    {
        std::vector<std::string> reports;
        {
            std::lock_guard<std::mutex> guard(reports_mutex_);
            reports.swap(reports_);
        }
        for (const std::string& report : reports)
            detail::InvokeIsolated([&] { host_.ReportTableError(report); });
    }

    std::string TablePipeline::Describe(TableDropReason reason) const
    {
        switch (reason)
        {
        case TableDropReason::NotRunning:
            return "Table rows were dropped: they were added while the collector was not running.";
        case TableDropReason::BufferFull:
            return "Table rows were dropped: the table row buffer of " + std::to_string(options_.max_buffer_bytes) +
                   " bytes was full (hsm_table_options_t.max_buffer_bytes).";
        case TableDropReason::SensorRefused:
            return "Table rows were dropped: they were added to a table sensor the server refused.";
        case TableDropReason::SensorUnavailable:
            return "Table rows were dropped: they were added to a table sensor this collector cannot send (see its state reason).";
        case TableDropReason::BacklogFull:
            return "Table rows were dropped: the undelivered table blocks passed " + std::to_string(options_.max_backlog_bytes) +
                   " bytes (hsm_table_options_t.max_backlog_bytes) and the oldest went.";
        case TableDropReason::RefusedByServer:
            return "Table rows were dropped: the server refused them in a table block.";
        case TableDropReason::NotDeliveredAtStop:
            return "Table rows were dropped: they were not delivered when the collector stopped.";
        case TableDropReason::RowTooLarge:
            return "Table rows were dropped: a row did not fit a frame of the table connection.";
        case TableDropReason::PackingFailed:
            return "Table rows were dropped: packing them into a block failed.";
        case TableDropReason::BufferGrowthFailed:
            return "Table rows were dropped: the table row buffer could not grow (out of memory).";
        default:
            return "Table rows were dropped.";
        }
    }

    std::vector<std::shared_ptr<TableSensor>> TablePipeline::SnapshotSensors() const
    {
        std::lock_guard<std::mutex> guard(sensors_mutex_);
        std::vector<std::shared_ptr<TableSensor>> sensors;
        sensors.reserve(sensors_.size());
        for (const auto& entry : sensors_)
            sensors.push_back(entry.second);
        return sensors;
    }

    bool TablePipeline::CreateSensor(const std::string& path, const std::vector<TableRegistrationColumn>& columns,
                                     const std::string& description, std::int64_t keep_history_ms,
                                     const std::string& unavailable_reason, std::shared_ptr<TableSensor>& out, std::string& error)
    {
        const TableBlockLimits limits;

        if (columns.empty())
        {
            error = "A table sensor needs at least one value column besides the time.";
            return false;
        }

        std::vector<TableSchemaColumn> schema_columns;
        schema_columns.reserve(columns.size() + 1);
        schema_columns.push_back({ "time", TableColumnType::Time });
        for (const TableRegistrationColumn& column : columns)
        {
            if (column.name == "time")
            {
                error = "The column name 'time' belongs to the time of the row, which every table sensor has first.";
                return false;
            }
            if (column.type != TableColumnType::Int64 && column.type != TableColumnType::Double)
            {
                error = "Column '" + column.name + "' has an unknown type; a value column is HSM_TABLE_COLUMN_DOUBLE or HSM_TABLE_COLUMN_INT64.";
                return false;
            }
            schema_columns.push_back({ column.name, column.type });
        }

        TableSchema schema;
        std::string schema_error;
        if (!TableSchema::Create(schema_columns, limits.max_columns_per_table, schema, schema_error))
        {
            error = "The columns of the table sensor are invalid: " + schema_error + ".";
            return false;
        }

        // A running pipeline decides by its run; a stopped one by what the collector has now (and
        // decides again at the next Start). Read before sensors_mutex_: Start takes the two the other way.
        std::string reason = unavailable_reason;
        bool running = false;
        {
            std::lock_guard<std::mutex> lifecycle(lifecycle_mutex_);
            running = running_;
            if (running_)
                reason = channel_ ? std::string() : channel_reason_;
        }

        std::lock_guard<std::mutex> guard(sensors_mutex_);
        const auto existing = sensors_.find(path);
        if (existing != sensors_.end())
        {
            if (existing->second->Schema().CanonicalBytes() != schema.CanonicalBytes())
            {
                error = "The table sensor " + path + " was created with other columns; another shape needs another path.";
                return false;
            }
            out = existing->second;
            return true;
        }

        auto sensor = std::make_shared<TableSensor>(shared_, path, columns, std::move(schema), description, keep_history_ms);

        if (!reason.empty())
        {
            sensor->SetState(HSM_TABLE_SENSOR_UNAVAILABLE, reason);
            Note("The table sensor " + path + " cannot send its rows: " + reason + ". Its rows are dropped and counted.");
        }

        sensors_.emplace(path, sensor);
        out = std::move(sensor);

        if (reason.empty() && running && !options_.manual_tick)
            RequestDelivery(/*registration=*/true, /*blocks=*/false);
        return true;
    }

    bool TablePipeline::Running() const
    {
        std::lock_guard<std::mutex> guard(lifecycle_mutex_);
        return running_;
    }

    void TablePipeline::Start(std::shared_ptr<ITableChannel> channel, const std::string& unavailable_reason)
    {
        {
            std::lock_guard<std::mutex> guard(lifecycle_mutex_);
            if (running_ || disposed_)
                return;

            if (channel)
                channel->Reset();
            channel_ = std::move(channel);
            channel_reason_ = channel_ ? std::string() : unavailable_reason;

            {
                std::lock_guard<std::mutex> pack(pack_mutex_);
                producer_ = NewProducerId();
                next_sequence_ = 0;
            }
            retry_not_before_ms_.store(0);
            stop_delivery_.store(false);
            {
                std::lock_guard<std::mutex> delivery(delivery_mutex_);
                register_due_ = false;
                deliver_due_ = false;
                drain_requested_ = false;
                drain_done_ = false;
            }

            // Every Start registers every sensor again; whether a sensor can send at all is decided now.
            for (const auto& sensor : SnapshotSensors())
            {
                const auto state = sensor->State();
                if (state == HSM_TABLE_SENSOR_REFUSED)
                    continue;
                if (!channel_)
                {
                    if (state != HSM_TABLE_SENSOR_UNAVAILABLE || sensor->Reason() != channel_reason_)
                    {
                        sensor->SetState(HSM_TABLE_SENSOR_UNAVAILABLE, channel_reason_);
                        Note("The table sensor " + sensor->Path() + " cannot send its rows: " + channel_reason_ + ". Its rows are dropped and counted.");
                    }
                }
                else
                {
                    sensor->SetState(HSM_TABLE_SENSOR_PENDING, std::string());
                }
            }

            shared_->ledger_open.store(true, std::memory_order_release);
            shared_->accepting.store(true, std::memory_order_release);
            run_count_.fetch_add(1);
            running_ = true;

            if (!options_.manual_tick)
            {
                last_tick_ms_.store(host_.TableSteadyNowMs());
                tick_ = std::make_unique<detail::ScheduledTask>(
                    [this] { return host_.TableSteadyNowMs(); },
                    [this] { return last_tick_ms_.load() + options_.tick_period_ms; },
                    [this] {
                        last_tick_ms_.store(host_.TableSteadyNowMs());
                        Tick();
                    });
                tick_->Start();

                if (channel_)
                {
                    delivery_thread_ = std::thread([this] { DeliveryLoop(); });
                    RequestDelivery(/*registration=*/true, /*blocks=*/false);
                }
            }
        }
        FlushReports();
    }

    void TablePipeline::BeginStop()
    {
        std::unique_ptr<detail::ScheduledTask> tick;
        {
            std::lock_guard<std::mutex> guard(lifecycle_mutex_);
            if (!running_)
                return;

            shared_->accepting.store(false, std::memory_order_release);
            tick = std::move(tick_);
        }
        // Joined with no lock of the pipeline held: a tick in progress may still report.
        if (tick)
            tick->Stop();

        // The final take: each sensor's mutex once. A writer that got in before is packed; one after
        // sees `accepting` false under the same mutex and is counted.
        try
        {
            Pack(/*final_pack=*/true);
        }
        catch (...)
        {
            Note("The final pack of the table sensors failed.");
        }
        ReportLosses();
    }

    void TablePipeline::StartDrain(std::int64_t budget_ms)
    {
        std::lock_guard<std::mutex> guard(lifecycle_mutex_);
        if (!running_)
            return;
        drain_deadline_ = std::chrono::steady_clock::now() + std::chrono::milliseconds(budget_ms);
        if (delivery_thread_.joinable())
        {
            std::lock_guard<std::mutex> delivery(delivery_mutex_);
            drain_requested_ = true;
            delivery_cv_.notify_all();
        }
    }

    void TablePipeline::FinishDrain()
    {
        std::thread delivery;
        std::shared_ptr<ITableChannel> channel;
        std::chrono::steady_clock::time_point deadline;
        {
            std::lock_guard<std::mutex> guard(lifecycle_mutex_);
            if (!running_)
                return;
            delivery = std::move(delivery_thread_);
            channel = channel_;
            deadline = drain_deadline_;
        }

        // No lock of the pipeline is held from here: the drain reports, and a host logger may call back.
        if (delivery.joinable())
        {
            bool done = false;
            {
                std::unique_lock<std::mutex> lock(delivery_mutex_);
                if (drain_requested_)
                    done = drained_cv_.wait_until(lock, deadline, [this] { return drain_done_; });
            }
            StopThread(delivery, channel, /*cancel=*/!done);
        }
        else if (channel)
        {
            // A manual tick (tests): the drain runs here, after the value drain, on the same budget.
            try
            {
                Deliver(/*at_stop=*/true, deadline);
            }
            catch (...)
            {
                Note("The table stop drain failed.");
            }
        }

        // What could not be delivered is dropped and counted; so are rows a sensor still holds (none
        // after BeginStop's final take: this is a guard).
        for (const auto& block : backlog_.TakeAll())
            block->DropRows(TableDropReason::NotDeliveredAtStop);
        for (const auto& sensor : SnapshotSensors())
            sensor->DiscardRows(TableDropReason::NotDeliveredAtStop);

        {
            std::lock_guard<std::mutex> guard(lifecycle_mutex_);
            running_ = false;
        }
        ReportLosses();
    }

    std::int64_t TablePipeline::CloseLedger()
    {
        shared_->ledger_open.store(false, std::memory_order_release);
        shared_->ledger.TakeReasons();
        return shared_->ledger.TakeUnreported();
    }

    void TablePipeline::StopThread(std::thread& delivery, const std::shared_ptr<ITableChannel>& channel, bool cancel)
    {
        {
            std::lock_guard<std::mutex> lock(delivery_mutex_);
            stop_delivery_.store(true);
            delivery_cv_.notify_all();
        }
        if (cancel && channel)
            channel->Cancel();
        if (delivery.joinable())
            delivery.join();
    }

    void TablePipeline::Dispose()
    {
        std::unique_ptr<detail::ScheduledTask> tick;
        std::thread delivery;
        std::shared_ptr<ITableChannel> channel;
        bool was_running = false;
        {
            std::lock_guard<std::mutex> guard(lifecycle_mutex_);
            if (disposed_)
                return;
            disposed_ = true;

            shared_->accepting.store(false, std::memory_order_release);
            tick = std::move(tick_);
            delivery = std::move(delivery_thread_);
            channel = channel_;
            was_running = running_;
            running_ = false;
        }

        if (tick)
            tick->Stop();
        if (delivery.joinable())
            StopThread(delivery, channel, /*cancel=*/true);

        if (was_running)
        {
            // The collector disposes after its stop; a pipeline still running here had none.
            for (const auto& block : backlog_.TakeAll())
                block->DropRows(TableDropReason::NotDeliveredAtStop);
            for (const auto& sensor : SnapshotSensors())
                sensor->DiscardRows(TableDropReason::NotDeliveredAtStop);
        }

        shared_->ledger_open.store(false, std::memory_order_release);
        shared_->disposed.store(true, std::memory_order_release);

        std::lock_guard<std::mutex> pack(pack_mutex_);
#if defined(HSM_COLLECTOR_TABLES)
        writer_.reset();
#endif
    }

    void TablePipeline::WakeTick()
    {
        std::lock_guard<std::mutex> guard(lifecycle_mutex_);
        if (tick_)
            tick_->Wake();
    }

    bool TablePipeline::TickAlive() const
    {
        std::lock_guard<std::mutex> guard(lifecycle_mutex_);
        return tick_ != nullptr;
    }

    bool TablePipeline::DeliveryThreadAlive() const
    {
        std::lock_guard<std::mutex> guard(lifecycle_mutex_);
        return delivery_thread_.joinable();
    }

    TableProducerId TablePipeline::Producer() const
    {
        std::lock_guard<std::mutex> guard(pack_mutex_);
        return producer_;
    }

    bool TablePipeline::HasWriter() const
    {
        std::lock_guard<std::mutex> guard(pack_mutex_);
#if defined(HSM_COLLECTOR_TABLES)
        return writer_ != nullptr;
#else
        return false;
#endif
    }

    // ---- Losses --------------------------------------------------------------------------------------

    void TablePipeline::ReportLosses()
    {
        try
        {
            for (const TableDropReason reason : shared_->ledger.TakeReasons())
                Note(Describe(reason));
        }
        catch (...)
        {
        }

        const std::int64_t rows = shared_->ledger.TakeUnreported();
        if (rows > 0)
        {
            bool taken = false;
            try
            {
                taken = host_.TryReportDroppedTableRows(rows);
            }
            catch (...)
            {
                taken = false;
            }
            if (!taken)
                shared_->ledger.GiveBack(rows);
        }

        FlushReports();
    }

    // ---- The tick ------------------------------------------------------------------------------------

    void TablePipeline::RunTickNow()
    {
        Tick();
    }

    void TablePipeline::Tick() noexcept
    {
        if (!shared_->accepting.load(std::memory_order_acquire))
            return;

        try
        {
            ReportLosses();

            if (options_.manual_tick)
            {
                RegisterPending();
                Pack(/*final_pack=*/false);
                Deliver(/*at_stop=*/false, std::chrono::steady_clock::time_point::max());
            }
            else
            {
                bool pending = false;
                for (const auto& sensor : SnapshotSensors())
                    pending = pending || sensor->State() == HSM_TABLE_SENSOR_PENDING;
                if (pending)
                    RequestDelivery(/*registration=*/true, /*blocks=*/false);
                Pack(/*final_pack=*/false);
                RequestDelivery(/*registration=*/false, /*blocks=*/true);
            }
        }
        catch (const std::exception& ex)
        {
            Note(std::string("The table tick failed: ") + ex.what());
        }
        catch (...)
        {
            Note("The table tick failed.");
        }
        FlushReports();
    }

    // ---- Registration --------------------------------------------------------------------------------

    void TablePipeline::RegisterPending()
    {
        {
            std::lock_guard<std::mutex> guard(register_mutex_);
            const std::shared_ptr<ITableChannel> channel = channel_;
            if (!channel)
                return;

            // In path order (the map's), so the frames of a large request are cut the same way every time.
            std::vector<std::shared_ptr<TableSensor>> batch;
            for (const auto& sensor : SnapshotSensors())
                if (sensor->State() == HSM_TABLE_SENSOR_PENDING)
                    batch.push_back(sensor);
            if (batch.empty())
                return;

            std::vector<TableRegistration> registrations;
            registrations.reserve(batch.size());
            for (const auto& sensor : batch)
                registrations.push_back(sensor->BuildRegistration());

            TableRegistrationResult result;
            try
            {
                result = channel->Register(registrations);
            }
            catch (...)
            {
                result = TableRegistrationResult{};
                result.unsettled_reason = "the table channel failed";
            }

            std::size_t unsettled = 0;
            for (const auto& sensor : batch)
            {
                if (result.accepted.count(sensor->Path()) != 0)
                {
                    sensor->TryMove(HSM_TABLE_SENSOR_PENDING, HSM_TABLE_SENSOR_REGISTERED);
                }
                else if (const auto refused = result.refused.find(sensor->Path()); refused != result.refused.end())
                {
                    if (sensor->TryMove(HSM_TABLE_SENSOR_PENDING, HSM_TABLE_SENSOR_REFUSED, refused->second))
                    {
                        Note("The server refused the table sensor " + sensor->Path() + ": " + refused->second +
                             ". It is not registered again; its rows are dropped and counted. Another shape or another place needs another sensor path.");
                        sensor->DiscardRows(TableDropReason::SensorRefused);
                    }
                }
                else
                {
                    ++unsettled;
                }
            }

            if (unsettled > 0)
                Note("Table sensor registrations were not delivered (" +
                     (result.unsettled_reason.empty() ? std::string("no answer for them") : result.unsettled_reason) +
                     "); they are sent again on the next tick and their rows are kept.");
        }
        FlushReports();
    }

    // ---- Packing -------------------------------------------------------------------------------------

    void TablePipeline::Pack(bool final_pack)
    {
        std::vector<std::pair<std::shared_ptr<TableSensor>, std::unique_ptr<TableRowBuffer>>> taken;
        {
            std::lock_guard<std::mutex> guard(pack_mutex_);
            if (!final_pack && !shared_->accepting.load(std::memory_order_acquire))
                return;

            for (const auto& sensor : SnapshotSensors())
            {
                if (sensor->State() != HSM_TABLE_SENSOR_REGISTERED)
                {
                    // At stop, the rows of a sensor that is not registered have no way out.
                    if (final_pack)
                        sensor->DiscardRows(TableDropReason::NotDeliveredAtStop);
                    continue;
                }
                std::unique_ptr<TableRowBuffer> rows = sensor->TakeRows();
                if (rows)
                    taken.emplace_back(sensor, std::move(rows));
            }

            if (!taken.empty())
            {
#if defined(HSM_COLLECTOR_TABLES)
                const std::shared_ptr<ITableChannel> channel = channel_;
                const TableBlockLimits limits = channel ? channel->Limits() : TableBlockLimits{};
                const std::size_t max_block = channel ? channel->MaxBlockBytes() : (std::numeric_limits<std::size_t>::max)() / 2;

                if (!writer_ || writer_->Limits() != limits)
                    writer_ = std::make_unique<TableBlockWriter>(limits);

                // The rows of each sensor in pieces that each fit the limits on their own: an upper
                // bound of a table's directory entry, and per row its values and its share of the maps.
                std::vector<Piece> pieces;
                for (auto& entry : taken)
                {
                    TableSensor& sensor = *entry.first;
                    const auto overhead = static_cast<std::int64_t>(32 + sensor.Path().size() + sensor.ColumnCount() + 1);
                    const auto per_row = static_cast<std::int64_t>(8 * (sensor.ColumnCount() + 1) + sensor.ColumnCount() + 1);
                    const std::int64_t by_bytes = (limits.max_decompressed_bytes - overhead) / per_row;
                    const auto max_rows = static_cast<std::size_t>((std::max)(std::int64_t{ 1 }, (std::min)(std::int64_t{ limits.max_rows }, by_bytes)));
                    const std::size_t count = entry.second->Count();
                    for (std::size_t start = 0; start < count; start += max_rows)
                        pieces.push_back(Piece{ &sensor, entry.second.get(), entry.first, start, (std::min)(max_rows, count - start) });
                }

                // Pieces in groups that keep to the limits, a table once per block.
                std::vector<std::vector<Piece>> groups;
                std::vector<Piece> group;
                std::int64_t group_rows = 0;
                std::int64_t group_bytes = 0;
                for (const Piece& piece : pieces)
                {
                    const auto overhead = static_cast<std::int64_t>(32 + piece.sensor->Path().size() + piece.sensor->ColumnCount() + 1);
                    const auto per_row = static_cast<std::int64_t>(8 * (piece.sensor->ColumnCount() + 1) + piece.sensor->ColumnCount() + 1);
                    const std::int64_t estimate = overhead + static_cast<std::int64_t>(piece.count) * per_row;
                    if (!group.empty() &&
                        (static_cast<std::int64_t>(group.size()) + 1 > limits.max_tables ||
                         group_rows + static_cast<std::int64_t>(piece.count) > limits.max_rows ||
                         group_bytes + estimate > limits.max_decompressed_bytes || group.back().sensor == piece.sensor))
                    {
                        groups.push_back(std::move(group));
                        group.clear();
                        group_rows = 0;
                        group_bytes = 0;
                    }
                    group.push_back(piece);
                    group_rows += static_cast<std::int64_t>(piece.count);
                    group_bytes += estimate;
                }
                if (!group.empty())
                    groups.push_back(std::move(group));

                for (const auto& each : groups)
                    EncodeAndQueue(each, max_block, limits);
#else
                // No block writer in this build: no sensor can be registered, so this is unreachable.
                for (auto& entry : taken)
                    entry.first->Drop(static_cast<std::int64_t>(entry.second->Count()), TableDropReason::PackingFailed);
#endif
            }

            for (auto& entry : taken)
            {
                shared_->Release(static_cast<std::int64_t>(entry.second->Count()) * entry.first->RowBytes());
                entry.first->ReturnRows(std::move(entry.second));
            }
        }
        FlushReports();
    }

#if defined(HSM_COLLECTOR_TABLES)
    // One block per group; a block larger than a frame allows is split, by tables first and then by
    // rows. Caller holds pack_mutex_.
    void TablePipeline::EncodeAndQueue(const std::vector<Piece>& group, std::size_t max_block, const TableBlockLimits& limits)
    {
        std::int64_t rows = 0;
        for (const Piece& piece : group)
            rows += static_cast<std::int64_t>(piece.count);

        std::vector<std::uint8_t> frame;
        std::string error;
        int status = 0;
        try
        {
            status = Encode(group, max_block, limits, frame, error);
        }
        catch (const std::bad_alloc&)
        {
            status = static_cast<int>(TableBlockStatus::OutOfMemory);
            error = "out of memory";
        }

        if (status == static_cast<int>(TableBlockStatus::Ok))
        {
            try
            {
                Enqueue(group, std::move(frame));
                return;
            }
            catch (const std::bad_alloc&)
            {
                status = static_cast<int>(TableBlockStatus::OutOfMemory);
                error = "out of memory";
            }
        }

        const bool too_large = status == static_cast<int>(TableBlockStatus::FrameTooLarge) || status == static_cast<int>(TableBlockStatus::LimitExceeded);
        if (too_large && group.size() > 1)
        {
            // Both halves are built before either is queued: a failure here queues nothing twice.
            const auto half = group.size() / 2;
            std::vector<Piece> first(group.begin(), group.begin() + static_cast<std::ptrdiff_t>(half));
            std::vector<Piece> second(group.begin() + static_cast<std::ptrdiff_t>(half), group.end());
            EncodeAndQueue(first, max_block, limits);
            EncodeAndQueue(second, max_block, limits);
            return;
        }
        if (too_large && group[0].count > 1)
        {
            const Piece& piece = group[0];
            const std::size_t first_rows = piece.count / 2;
            std::vector<Piece> first{ Piece{ piece.sensor, piece.buffer, piece.owner, piece.start, first_rows } };
            std::vector<Piece> second{ Piece{ piece.sensor, piece.buffer, piece.owner, piece.start + first_rows, piece.count - first_rows } };
            EncodeAndQueue(first, max_block, limits);
            EncodeAndQueue(second, max_block, limits);
            return;
        }
        if (too_large)
        {
            group[0].sensor->Drop(1, TableDropReason::RowTooLarge);
            Note("A row of the table sensor " + group[0].sensor->Path() + " does not fit a frame of the table connection (" +
                 std::to_string(max_block) + " bytes) and is dropped.");
            return;
        }

        for (const Piece& piece : group)
            piece.sensor->Drop(static_cast<std::int64_t>(piece.count), TableDropReason::PackingFailed);
        Note("Packing " + std::to_string(rows) + " table row(s) into a block failed (" + error + "); they are dropped and counted.");
    }

    int TablePipeline::Encode(const std::vector<Piece>& group, std::size_t max_block, const TableBlockLimits& limits,
                              std::vector<std::uint8_t>& frame, std::string& error)
    {
        (void)limits;
        TableBlockWriter& writer = *writer_;
        writer.Reset();

        TableBlockStatus status = TableBlockStatus::Ok;
        for (const Piece& piece : group)
        {
            const TableSensor& sensor = *piece.sensor;
            const TableRowBuffer& rows = *piece.buffer;
            status = writer.BeginTable(reinterpret_cast<const std::uint8_t*>(sensor.Path().data()), sensor.Path().size(),
                                       sensor.Schema().Id(), static_cast<std::int64_t>(piece.count));
            if (status == TableBlockStatus::Ok)
                status = writer.AddColumn(TableColumnType::Time, rows.Times() + piece.start, piece.count);
            for (std::size_t column = 0; status == TableBlockStatus::Ok && column < sensor.ColumnCount(); ++column)
            {
                const std::size_t map_length = (piece.count + 7) / 8;
                null_map_.assign(map_length, 0);
                const bool has_nulls = rows.CopyNullMap(column, piece.start, piece.count, null_map_.data());
                status = writer.AddColumn(sensor.ColumnType(column), rows.Values(column) + piece.start, piece.count,
                                          has_nulls ? null_map_.data() : nullptr, has_nulls ? map_length : 0);
            }
            if (status != TableBlockStatus::Ok)
                break;
        }

        if (status == TableBlockStatus::Ok)
            status = writer.Finish(frame, kTableFrameHeaderBytes, static_cast<std::uint64_t>(max_block) + kTableFrameHeaderBytes);
        if (status != TableBlockStatus::Ok)
            error = writer.LastError();
        writer.Reset();
        return static_cast<int>(status);
    }

    // Caller holds pack_mutex_, so the sequence rises in the order of the blocks.
    void TablePipeline::Enqueue(const std::vector<Piece>& group, std::vector<std::uint8_t> frame)
    {
        auto block = std::make_shared<PendingBlock>();
        block->frame = std::move(frame);
        block->tables.reserve(group.size());
        for (const Piece& piece : group)
        {
            block->tables.push_back(PendingBlock::Table{ piece.owner, static_cast<std::int64_t>(piece.count) });
            block->rows += static_cast<std::int64_t>(piece.count);
        }
        block->sequence = next_sequence_++;

        for (const auto& dropped : backlog_.Append(std::move(block)))
            dropped->DropRows(TableDropReason::BacklogFull);
    }
#endif

    // ---- Delivery ------------------------------------------------------------------------------------

    void TablePipeline::RequestDelivery(bool registration, bool blocks)
    {
        std::lock_guard<std::mutex> guard(delivery_mutex_);
        register_due_ = register_due_ || registration;
        deliver_due_ = deliver_due_ || blocks;
        delivery_cv_.notify_all();
    }

    void TablePipeline::DeliveryLoop()
    {
        std::unique_lock<std::mutex> lock(delivery_mutex_);
        while (true)
        {
            delivery_cv_.wait(lock, [this] { return stop_delivery_.load() || drain_requested_ || register_due_ || deliver_due_; });
            if (stop_delivery_.load())
                return;

            if (drain_requested_)
            {
                const auto deadline = drain_deadline_;
                lock.unlock();
                try
                {
                    Deliver(/*at_stop=*/true, deadline);
                }
                catch (...)
                {
                    Note("The table stop drain failed.");
                }
                lock.lock();
                drain_done_ = true;
                drained_cv_.notify_all();
                // The stop takes it from here; wait for its word to end.
                delivery_cv_.wait(lock, [this] { return stop_delivery_.load(); });
                return;
            }

            const bool registration = std::exchange(register_due_, false);
            const bool blocks = std::exchange(deliver_due_, false);
            lock.unlock();
            try
            {
                if (registration)
                    RegisterPending();
                if (blocks)
                    Deliver(/*at_stop=*/false, std::chrono::steady_clock::time_point::max());
            }
            catch (...)
            {
                Note("The table delivery failed.");
                FlushReports();
            }
            lock.lock();
        }
    }

    // Sends the backlog from its head, one block at a time, each after the answer to the one before,
    // until it is empty or a block is not delivered. An answer that came back is applied whatever
    // happens meanwhile: a block the server stored is never counted as lost.
    void TablePipeline::Deliver(bool at_stop, std::chrono::steady_clock::time_point deadline)
    {
        {
            std::lock_guard<std::mutex> gate(deliver_mutex_);
            const std::shared_ptr<ITableChannel> channel = channel_;
            if (!channel)
                return;

            while (true)
            {
                if (stop_delivery_.load())
                    break;
                if (at_stop && std::chrono::steady_clock::now() >= deadline)
                    break;
                if (!at_stop && host_.TableSteadyNowMs() < retry_not_before_ms_.load())
                    break;

                const std::shared_ptr<PendingBlock> head = backlog_.BeginHead();
                if (!head)
                    break;

                TableBlockAck ack;
                try
                {
                    ack = channel->Put(producer_, head->sequence, head->frame);
                }
                catch (...)
                {
                    ack = TableBlockAck{};
                    ack.error = "the table channel failed";
                }

                if (on_ack_for_tests)
                    on_ack_for_tests();

                if (ack.outcome == TableBlockOutcome::NotDelivered)
                {
                    backlog_.EndFlight(head);
                    if (ack.retry_after_ms >= 0)
                        retry_not_before_ms_.store(host_.TableSteadyNowMs() + (std::min)(ack.retry_after_ms, kMaxRetryAfterMs));
                    Note("Table blocks are not delivered: " + (ack.error.empty() ? std::string("no answer") : ack.error) +
                         ". They are kept and sent again.");
                    break;
                }

                Settle(head, ack);
            }
        }
        FlushReports();
    }

    // A block with a final answer leaves the backlog; its refused tables are counted and acted on. A
    // block the bound or a stop dropped already was counted there and is not counted again.
    void TablePipeline::Settle(const std::shared_ptr<PendingBlock>& block, const TableBlockAck& ack)
    {
        if (!backlog_.Settle(block))
            return;

        switch (ack.outcome)
        {
        case TableBlockOutcome::Stored:
        case TableBlockOutcome::Duplicate:
            ApplyRefusals(*block, ack, /*count_rows=*/true);
            break;
        case TableBlockOutcome::AllRefused:
            ApplyRefusals(*block, ack, /*count_rows=*/false);
            block->DropRows(TableDropReason::RefusedByServer);
            break;
        case TableBlockOutcome::Invalid:
            block->DropRows(TableDropReason::RefusedByServer);
            Note("The server refused a table block as invalid (" + ack.error + "); its " + std::to_string(block->rows) +
                 " row(s) are lost and it is not sent again.");
            break;
        case TableBlockOutcome::TooLarge:
            block->DropRows(TableDropReason::RefusedByServer);
            Note("The table connection refused a block of " + std::to_string(block->BlockBytes()) +
                 " bytes as larger than its frame limit; its " + std::to_string(block->rows) + " row(s) are lost.");
            break;
        case TableBlockOutcome::NotDelivered:
            break;
        }
    }

    void TablePipeline::ApplyRefusals(const PendingBlock& block, const TableBlockAck& ack, bool count_rows)
    {
        bool registration = false;
        for (const TableRefusal& refusal : ack.refused)
        {
            if (refusal.index < 0 || static_cast<std::size_t>(refusal.index) >= block.tables.size())
            {
                Note("The server refused table " + std::to_string(refusal.index) + " (" + refusal.table + ") of a block of " +
                     std::to_string(block.tables.size()) + " table(s): " + refusal.reason + ".");
                continue;
            }

            const PendingBlock::Table& table = block.tables[static_cast<std::size_t>(refusal.index)];
            TableSensor& sensor = *table.sensor;
            if (count_rows)
                sensor.Drop(table.rows, TableDropReason::RefusedByServer);

            if (refusal.reason == "UnknownSensor")
            {
                // An operator may have removed the sensor: it is registered again; the rows of this block are lost.
                if (sensor.TryMove(HSM_TABLE_SENSOR_REGISTERED, HSM_TABLE_SENSOR_PENDING))
                {
                    Note("The server does not know the table sensor " + sensor.Path() +
                         " any more; it is registered again. The rows of the refused block are lost.");
                    registration = true;
                }
            }
            else if (refusal.reason == "SensorBlocked" || refusal.reason == "SensorCreatedAfterBlock")
            {
                Note("The server refused the rows of the table sensor " + sensor.Path() + " in a block: " + refusal.reason + ".");
            }
            else if (refusal.reason == "NotATableSensor" || refusal.reason == "SchemaIdMismatch" ||
                     refusal.reason == "ColumnTypesMismatch" || refusal.reason == "InvalidIdentifier")
            {
                if (sensor.TryMove(HSM_TABLE_SENSOR_REGISTERED, HSM_TABLE_SENSOR_REFUSED, "the server refused its rows: " + refusal.reason))
                {
                    Note("The server refused the rows of the table sensor " + sensor.Path() + ": " + refusal.reason +
                         ". Its rows are dropped and counted from now on.");
                    sensor.DiscardRows(TableDropReason::SensorRefused);
                }
            }
            else
            {
                Note("The server refused the rows of the table sensor " + sensor.Path() +
                     " for a reason this collector does not know: " + refusal.reason + ".");
            }
        }

        if (ack.refused_count > static_cast<std::int32_t>(ack.refused.size()))
            Note("The server refused " + std::to_string(ack.refused_count) + " table(s) of a block and listed " +
                 std::to_string(ack.refused.size()) + "; the rows of the others are lost too.");

        if (registration && !options_.manual_tick)
            RequestDelivery(/*registration=*/true, /*blocks=*/false);
    }
} // namespace hsm::collector::tables
