using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tables;
using HSMDataCollector.Tests.Tables;
using HSMSensorDataObjects.SensorRequests;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// The table sensor verbs of the conformance corpus (#1846, <c>tests/conformance/collector/table_sensor_contract.hsmtest</c>).
    /// The scripted server side is the recording sender's table channel; the tick is driven by <c>table_tick</c>.
    /// Rows are compared decoded, never as bytes: two zstd libraries may compress the same block differently (ADR-0021).
    /// </summary>
    public sealed partial class CollectorConformanceTests
    {
        private static readonly DateTime TableEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static async Task<bool> ExecuteTableStepAsync(ContractState state, ContractStep step)
        {
            switch (step.Action)
            {
                case "create_collector_without_table_transport":
                    state.Sender = new RecordingSender { TablesUnavailable = true };
                    state.Collector = CreateCollector(state.Sender);
                    return true;

                case "create_table_sensor":
                    CreateTableSensor(state, step, SensorLocation.Module);
                    return true;

                case "create_table_sensor_at_product":
                    CreateTableSensor(state, step, SensorLocation.Product);
                    return true;

                case "expect_create_table_sensor_rejected":
                    Assert.ThrowsAny<ArgumentException>(() => state.Collector.CreateTableSensor(ExpandTextToken(step.Arg(0)), TableOptions(step.Arg(1), SensorLocation.Module)));
                    return true;

                case "add_table_row":
                {
                    var table = Table(state, step.Arg(0));
                    var cells = Enumerable.Range(2, table.Columns.Count).Select(i => Cell(table, i - 2, step.Arg(i))).ToArray();
                    state.TableResults.Add(table.AddRow(TableTime(step.Arg(1)), cells));
                    return true;
                }

                case "add_table_rows":
                {
                    // path|count|start_ms: row r has the time start_ms + r and the value r in every column.
                    var table = Table(state, step.Arg(0));
                    var count = int.Parse(step.Arg(1), CultureInfo.InvariantCulture);
                    var start = TableTime(step.Arg(2));
                    var times = new DateTime[count];
                    var cells = new TableCell[count * table.Columns.Count];

                    for (var row = 0; row < count; row++)
                    {
                        times[row] = start.AddMilliseconds(row);

                        for (var column = 0; column < table.Columns.Count; column++)
                            cells[row * table.Columns.Count + column] = (long)row;
                    }

                    state.TableResults.Add(table.AddRows(times, cells, count) == count);
                    return true;
                }

                case "table_tick":
                    await state.Collector.TickTablesAsync().ConfigureAwait(false);
                    return true;

                case "table_server_refuse_registration":
                    state.Sender.Tables.RefuseRegistration(TablePathOf(state, step.Arg(0)), step.Arg(1));
                    return true;

                case "table_server_fail_registrations":
                    state.Sender.Tables.FailRegistrations(int.Parse(step.Arg(0), CultureInfo.InvariantCulture));
                    return true;

                case "table_server_down":
                    state.Sender.Tables.SetDown(true);
                    return true;

                case "table_server_up":
                    state.Sender.Tables.SetDown(false);
                    return true;

                case "table_server_frame_limit":
                    state.Sender.Tables.MaxBlockBytes = int.Parse(step.Arg(0), CultureInfo.InvariantCulture) - 40;
                    return true;

                case "table_server_answer_next":
                    state.Sender.Tables.AnswerNext(ScriptedAnswer(step.Arg(0)));
                    return true;

                case "table_server_refuse_table_next":
                {
                    var path = TablePathOf(state, step.Arg(0));
                    var reason = step.Arg(1);

                    state.Sender.Tables.AnswerNext(block => TableBlockAck.Stored(new[] { new TableRefusal(block.TableNames.ToList().IndexOf(path), path, reason) }, 1));
                    return true;
                }

                case "expect_table_add_results":
                    // The results of the add verbs so far, in order: 1 taken, 0 dropped.
                    Assert.Equal(step.Arg(0), string.Concat(state.TableResults.Select(r => r ? "1" : "0")));
                    return true;

                case "expect_table_path":
                    Assert.Equal(step.Arg(1), Table(state, step.Arg(0)).Path);
                    return true;

                case "expect_table_state":
                    Assert.Equal(step.Arg(1), Table(state, step.Arg(0)).State.ToString().ToLowerInvariant());
                    return true;

                case "expect_table_rows_accepted":
                    Assert.Equal(long.Parse(step.Arg(1), CultureInfo.InvariantCulture), Table(state, step.Arg(0)).RowsAccepted);
                    return true;

                case "expect_table_rows_dropped":
                    Assert.Equal(long.Parse(step.Arg(1), CultureInfo.InvariantCulture), Table(state, step.Arg(0)).RowsDropped);
                    return true;

                case "expect_table_registration_count":
                    Assert.Equal(int.Parse(step.Arg(0), CultureInfo.InvariantCulture), state.Sender.Tables.Registrations.Count);
                    return true;

                case "expect_table_registration":
                {
                    // index|path|columns: request `index` registers `path` with these columns (the time first).
                    var request = state.Sender.Tables.Registrations[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)];
                    var registration = Assert.Single(request, r => r.Path == TablePathOf(state, step.Arg(1)));

                    Assert.Equal(11, (int)registration.SensorType);
                    Assert.Equal(step.Arg(2), string.Join(",", registration.TableColumns.Select(c => $"{c.Name}:{ColumnTypeName(c.Type)}")));
                    return true;
                }

                case "expect_table_registration_size":
                    // index|count: request `index` carries this many table registrations and nothing else.
                    Assert.Equal(int.Parse(step.Arg(1), CultureInfo.InvariantCulture), state.Sender.Tables.Registrations[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)].Count);
                    return true;

                case "expect_table_blocks":
                    Assert.Equal(int.Parse(step.Arg(0), CultureInfo.InvariantCulture), state.Sender.Tables.Stored.Count);
                    return true;

                case "expect_table_block_attempts":
                    Assert.Equal(int.Parse(step.Arg(0), CultureInfo.InvariantCulture), state.Sender.Tables.Attempts.Count);
                    return true;

                case "expect_table_block_tables":
                    Assert.Equal(int.Parse(step.Arg(1), CultureInfo.InvariantCulture), state.Sender.Tables.Stored[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)].Block.Tables.Count);
                    return true;

                case "expect_table_blocks_within":
                    Assert.All(state.Sender.Tables.Attempts, b => Assert.True(b.Bytes.Length + 40 <= int.Parse(step.Arg(0), CultureInfo.InvariantCulture), $"a frame of {b.Bytes.Length + 40} bytes"));
                    return true;

                case "expect_table_sequences":
                    // The producer sequences of every attempt, in order; one producer throughout.
                    Assert.Equal(step.Arg(0), string.Join(",", state.Sender.Tables.Attempts.Select(a => a.Sequence)));
                    Assert.Single(state.Sender.Tables.Attempts.Select(a => a.Producer).Distinct());
                    return true;

                case "expect_table_sequences_increasing":
                {
                    // Every attempt has a sequence above the one before, or repeats it (a resend); one producer throughout.
                    var sequences = state.Sender.Tables.Attempts.Select(a => a.Sequence).ToList();

                    for (var i = 1; i < sequences.Count; i++)
                        Assert.True(sequences[i] >= sequences[i - 1], $"sequence {sequences[i]} after {sequences[i - 1]}");

                    Assert.Single(state.Sender.Tables.Attempts.Select(a => a.Producer).Distinct());
                    return true;
                }

                case "expect_table_rows":
                    Assert.Equal(int.Parse(step.Arg(1), CultureInfo.InvariantCulture), state.Sender.Tables.RowsOf(TablePathOf(state, step.Arg(0))).Count);
                    return true;

                case "expect_table_row":
                {
                    // path|index|time_ms|v1|v2...: the index-th delivered row of the table.
                    var table = Table(state, step.Arg(0));
                    var row = state.Sender.Tables.RowsOf(table.Path)[int.Parse(step.Arg(1), CultureInfo.InvariantCulture)];

                    Assert.Equal(TableTime(step.Arg(2)).Ticks, row.Ticks);

                    for (var column = 0; column < table.Columns.Count; column++)
                    {
                        var expected = ExpandTextToken(step.Arg(3 + column));

                        if (expected == null)
                            Assert.Null(row.Values[column]);
                        else
                            Assert.Equal(ParseTableNumber(expected), row.Values[column]);
                    }

                    return true;
                }
            }

            return false;
        }

        private static void CreateTableSensor(ContractState state, ContractStep step, SensorLocation location)
        {
            var name = step.Arg(0);
            state.TableSensors[name] = state.Collector.CreateTableSensor(ExpandTextToken(name), TableOptions(step.Arg(1), location));
        }

        // name:type pairs, comma-separated; type double or int64. "none" declares no column.
        private static TableSensorOptions TableOptions(string columns, SensorLocation location)
        {
            var options = new TableSensorOptions { SensorLocation = location };

            if (columns == "none")
                return options;

            foreach (var column in columns.Split(','))
            {
                var parts = column.Split(':');

                switch (parts[1])
                {
                    case "double": options.Columns.Add(TableColumn.Double(parts[0])); break;
                    case "int64": options.Columns.Add(TableColumn.Int64(parts[0])); break;
                    default: throw new InvalidOperationException($"Unknown table column type '{parts[1]}' (double, int64).");
                }
            }

            return options;
        }

        private static ITableSensor Table(ContractState state, string name) =>
            state.TableSensors.TryGetValue(name, out var table) ? table : throw new InvalidOperationException($"No table sensor '{name}' was created in this case.");

        private static string TablePathOf(ContractState state, string name) => Table(state, name).Path;

        private static DateTime TableTime(string milliseconds) =>
            TableEpoch.AddMilliseconds(long.Parse(milliseconds, CultureInfo.InvariantCulture));

        private static TableCell Cell(ITableSensor table, int column, string text)
        {
            var value = ExpandTextToken(text);

            if (value == null)
                return TableCell.Null;

            return table.Columns[column].Type == TableColumnType.Int64
                ? TableCell.FromInt64(long.Parse(value, CultureInfo.InvariantCulture))
                : TableCell.FromDouble(ParseTableNumber(value));
        }

        private static double ParseTableNumber(string value)
        {
            switch (value)
            {
                case "NaN": return double.NaN;
                case "Infinity": return double.PositiveInfinity;
                case "-Infinity": return double.NegativeInfinity;
                default: return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        private static string ColumnTypeName(TableColumnType type)
        {
            switch (type)
            {
                case TableColumnType.Time: return "time";
                case TableColumnType.Int64: return "int64";
                case TableColumnType.Double: return "double";
                default: return type.ToString();
            }
        }

        private static Func<FakeTableSender.SentBlock, TableBlockAck> ScriptedAnswer(string outcome)
        {
            switch (outcome)
            {
                case "stored": return _ => TableBlockAck.Stored();
                case "duplicate": return _ => TableBlockAck.Duplicate();
                case "all_refused": return block => TableBlockAck.AllRefused(block.TableNames.Select((t, i) => new TableRefusal(i, t, "SensorCreatedAfterBlock")).ToList(), block.TableNames.Count);
                case "invalid": return _ => TableBlockAck.Invalid("invalid block: scripted");
                case "not_delivered": return _ => TableBlockAck.NotDelivered("scripted: 503", TimeSpan.Zero);
                default: throw new InvalidOperationException($"Unknown block answer '{outcome}' (stored, duplicate, all_refused, invalid, not_delivered).");
            }
        }


        private sealed partial class ContractState
        {
            public Dictionary<string, ITableSensor> TableSensors { get; } = new Dictionary<string, ITableSensor>(StringComparer.Ordinal);

            public List<bool> TableResults { get; } = new List<bool>();
        }


        // The recording sender carries tables through a scripted table channel of its own.
        private sealed partial class RecordingSender : ITableBlockChannel
        {
            public FakeTableSender Tables { get; } = new FakeTableSender();

            // Set before the collector is built: the collector then has no table transport.
            public bool TablesUnavailable { get; set; }

            string ITableBlockChannel.TablesUnavailableReason => TablesUnavailable ? "this conformance sender has no table transport" : null;

            int ITableBlockChannel.MaxBlockBytes => Tables.MaxBlockBytes;

            HSMTableBlocks.TableBlockLimits ITableBlockChannel.BlockLimits => Tables.BlockLimits;

            Task ITableBlockChannel.WaitUntilReadyAsync(CancellationToken token) => Tables.WaitUntilReadyAsync(token);

            Task<TableRegistrationResult> ITableBlockChannel.RegisterTablesAsync(IReadOnlyList<AddOrUpdateSensorRequest> registrations, CancellationToken token) =>
                Tables.RegisterTablesAsync(registrations, token);

            Task<TableBlockAck> ITableBlockChannel.PutBlockAsync(Guid producer, long sequence, byte[] block, CancellationToken token) =>
                Tables.PutBlockAsync(producer, sequence, block, token);

            Task ITableBlockChannel.CloseAsync(CancellationToken budget) => Task.CompletedTask;
        }
    }
}
