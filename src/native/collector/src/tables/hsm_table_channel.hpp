#pragma once

/// @file
/// @brief The seam between the table pipeline and whatever carries its registrations and blocks to the
/// server (#1848). Internal, never part of the public headers; the managed counterpart is
/// HSMDataCollector.Tables.ITableBlockChannel.
///
/// In 0.15.0 the only implementations are the tests' recording channel; a real host has no channel and
/// its table sensors are Unavailable. The channel over the collector session's table connection is the
/// next step of #1848.

#include "hsm_table_format.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <map>
#include <set>
#include <string>
#include <vector>

namespace hsm::collector::tables
{
    /// The bytes in front of every block that the channel fills with its frame header (wire contract,
    /// "Table connection"). The pipeline reserves them so a block is sent without a copy.
    constexpr std::size_t kTableFrameHeaderBytes = 40;

    using TableProducerId = std::array<std::uint8_t, 16>;

    struct TableRegistrationColumn
    {
        std::string name;
        TableColumnType type;
    };

    /// One table sensor's registration: the managed AddOrUpdateSensorRequest with SensorType 11.
    struct TableRegistration
    {
        std::string path;
        /// The time first, then the value columns.
        std::vector<TableRegistrationColumn> columns;
        std::string description;
        /// 0: none sent (the server's default).
        std::int64_t keep_history_ms = 0;
    };

    /// The answer to one registration request, by path.
    struct TableRegistrationResult
    {
        std::set<std::string> accepted;
        /// Refused for good, with the server's words.
        std::map<std::string, std::string> refused;
        /// Why the paths in neither set are unsettled (no session, no answer, a fault).
        std::string unsettled_reason;
    };

    enum class TableBlockOutcome
    {
        /// No answer, or a fault of the server (503, 500): keep the block and send it again.
        NotDelivered = 0,
        /// Stored; the tables listed as refused were not.
        Stored,
        /// This producer's block with this sequence was stored before; settled.
        Duplicate,
        /// Every table was refused and nothing was stored. Final.
        AllRefused,
        /// A 400: the block breaks the format or the limits. Final.
        Invalid,
        /// The frame is larger than the connection takes now (its limit shrank after the block was
        /// packed). Final for these bytes.
        TooLarge,
    };

    struct TableRefusal
    {
        /// The table's index in the block's directory.
        std::int32_t index = -1;
        std::string table;
        std::string reason;
    };

    /// The server's answer to one block.
    struct TableBlockAck
    {
        TableBlockOutcome outcome = TableBlockOutcome::NotDelivered;
        /// The refused tables the answer lists (at most the first 100).
        std::vector<TableRefusal> refused;
        /// The exact number of refused tables, which may exceed the listed ones.
        std::int32_t refused_count = 0;
        /// The server's text, or why there was no answer.
        std::string error;
        /// How long to wait before the next attempt when the server said so; -1 when it did not.
        std::int64_t retry_after_ms = -1;
    };

    /// What carries table sensors to the server. Called by the pipeline's own threads only (the
    /// delivery thread, or the caller of a manual tick in tests), one call at a time; Cancel from any
    /// thread. No call may throw (the pipeline catches and counts a throw as no answer all the same).
    class ITableChannel
    {
    public:
        virtual ~ITableChannel() = default;

        /// The largest block one frame may carry now, the frame header not included.
        virtual std::size_t MaxBlockBytes() const = 0;

        /// The limits a block is checked against by the server.
        virtual TableBlockLimits Limits() const = 0;

        /// Sends the registrations of table sensors as one request of their own (never mixed with
        /// other sensors) and says for each path whether it was accepted, refused for good or is
        /// unsettled.
        virtual TableRegistrationResult Register(const std::vector<TableRegistration>& registrations) = 0;

        /// Puts one block under the producer's id and sequence and returns the server's answer.
        /// `frame` holds kTableFrameHeaderBytes reserved bytes, then the block; the channel may write
        /// its header into the reserved bytes.
        virtual TableBlockAck Put(const TableProducerId& producer, std::int64_t sequence, std::vector<std::uint8_t>& frame) = 0;

        /// Ends a Register or Put in flight at once (it answers "not delivered"/unsettled), and every
        /// later one until Reset. Any thread.
        virtual void Cancel() = 0;

        /// Called at every Start before the first call of the run.
        virtual void Reset() = 0;
    };
} // namespace hsm::collector::tables
