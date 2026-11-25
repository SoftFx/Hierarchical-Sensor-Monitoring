using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tests.Transport;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// The table connection verbs of the conformance corpus (#1846 part 2,
    /// <c>tests/conformance/collector/table_connection_contract.hsmtest</c>): on a collector whose transport is the
    /// session, the scripted session server takes the table connection and stands in for the recording sender's table
    /// channel in the block verbs of <c>table_sensor_contract.hsmtest</c>.
    /// </summary>
    public sealed partial class CollectorConformanceTests
    {
        // True when the case's collector is on the scripted session (create_collector_on_session).
        private static bool OnSession(ContractState state) => state.Sender == null && state.SessionServer != null;

        private static ScriptedSessionServer TableServer(ContractState state, string verb) =>
            OnSession(state) ? state.SessionServer : throw new InvalidOperationException($"{verb} needs a collector on the scripted session (create_collector_on_session).");

        // Null when the verb is not a table connection verb, or is one of the block verbs that a recording sender serves.
        private static async Task<bool> ExecuteTableConnectionStepAsync(ContractState state, ContractStep step)
        {
            var onSession = OnSession(state);

            switch (step.Action)
            {
                case "table_register":
                {
                    var table = Table(state, step.Arg(0));

                    Assert.True(await PollAsync(() =>
                    {
                        if (table.State != TableSensorState.Pending)
                            return true;

                        state.Collector.TickTablesAsync().GetAwaiter().GetResult();
                        return table.State != TableSensorState.Pending;
                    }, step.Arg(1)).ConfigureAwait(false), $"table_register: {table.Path} is still pending.");

                    return true;
                }

                case "table_server_refuse_upgrades":
                    TableServer(state, step.Action).RefuseTableUpgrades(int.Parse(step.Arg(0), CultureInfo.InvariantCulture));
                    return true;

                case "table_server_accept_upgrades":
                    TableServer(state, step.Action).AcceptTableUpgrades();
                    return true;

                case "expect_table_connections":
                {
                    var server = TableServer(state, step.Action);
                    var expected = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);

                    await PollAsync(() => server.TableConnections >= expected, step.Arg(1)).ConfigureAwait(false);
                    Assert.Equal(expected, server.TableConnections);
                    return true;
                }

                case "expect_refused_table_upgrades_at_least":
                {
                    var server = TableServer(state, step.Action);
                    var expected = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);

                    Assert.True(await PollAsync(() => server.RefusedTableUpgrades >= expected, step.Arg(1)).ConfigureAwait(false),
                                $"Expected at least {expected} refused table upgrade(s), got {server.RefusedTableUpgrades}.");
                    return true;
                }

                case "expect_table_upgrade_names_session":
                {
                    var server = TableServer(state, step.Action);
                    var upgrade = server.TableUpgradeHeaders[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)];

                    Assert.Equal(server.SessionIds[int.Parse(step.Arg(1), CultureInfo.InvariantCulture)], upgrade["X-Hsm-Session"]);
                    return true;
                }

                case "expect_table_probe_before_blocks":
                {
                    var server = TableServer(state, step.Action);
                    var frames = server.TableFrames;
                    var connections = frames.Select(f => f.Connection).Distinct().Count();

                    Assert.True(server.TableProbes.Count >= connections, $"{server.TableProbes.Count} probe(s) for {connections} table connection(s) that carried blocks");
                    Assert.True(frames.Count == 0 || server.TableProbes.Min() < frames.Min(f => f.Id), "a block came before the first probe");
                    return true;
                }

                case "expect_table_frame_connections":
                    Assert.Equal(step.Arg(0), string.Join(",", TableServer(state, step.Action).TableFrames.Select(f => f.Connection)));
                    return true;

                case "expect_table_close_code":
                {
                    var server = TableServer(state, step.Action);
                    var code = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);

                    Assert.True(await PollAsync(() => server.TableCloses.Contains(code), step.Arg(1)).ConfigureAwait(false),
                                $"The collector closed no table connection with {code}; closes: {string.Join(",", server.TableCloses)}.");
                    return true;
                }

                case "expect_table_closed_before_session":
                {
                    var server = TableServer(state, step.Action);

                    Assert.True(await PollAsync(() => server.CloseOrder.Contains("table") && server.CloseOrder.Contains("session"), step.Arg(0)).ConfigureAwait(false),
                                $"The collector did not close both connections; closes: {string.Join(", ", server.CloseOrder)}.");
                    Assert.True(server.CloseOrder.IndexOf("table") < server.CloseOrder.IndexOf("session"), $"closes in the order {string.Join(", ", server.CloseOrder)}");
                    return true;
                }
            }

            if (!onSession)
                return false;

            // The block verbs of table_sensor_contract.hsmtest, against the scripted server.
            var tables = state.SessionServer;

            switch (step.Action)
            {
                case "table_server_frame_limit":
                    tables.TableMaxFrameBytes = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);
                    return true;

                case "table_server_answer_next":
                    tables.AnswerTableNext(SessionAnswer(step.Arg(0)));
                    return true;

                case "table_server_refuse_table_next":
                    tables.AnswerTableNext(ScriptedSessionServer.TableAnswer.Store(new Dictionary<string, string> { [TablePathOf(state, step.Arg(0))] = step.Arg(1) }));
                    return true;

                case "expect_table_block_attempts":
                    Assert.Equal(int.Parse(step.Arg(0), CultureInfo.InvariantCulture), tables.TableFrames.Count);
                    return true;

                case "expect_table_blocks_within":
                {
                    var limit = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);
                    Assert.All(tables.TableFrames, f => Assert.True(f.FrameBytes <= limit, $"a frame of {f.FrameBytes} bytes"));
                    return true;
                }

                case "expect_table_sequences":
                    Assert.Equal(step.Arg(0), string.Join(",", tables.TableFrames.Select(f => f.Sequence)));
                    Assert.Single(tables.TableFrames.Select(f => f.Producer).Distinct());
                    return true;

                case "expect_table_sequences_increasing":
                {
                    var sequences = tables.TableFrames.Select(f => f.Sequence).ToList();

                    for (var i = 1; i < sequences.Count; i++)
                        Assert.True(sequences[i] >= sequences[i - 1], $"sequence {sequences[i]} after {sequences[i - 1]}");

                    Assert.Single(tables.TableFrames.Select(f => f.Producer).Distinct());
                    return true;
                }

                case "expect_table_rows":
                    Assert.Equal(int.Parse(step.Arg(1), CultureInfo.InvariantCulture), tables.StoredTableRowsOf(TablePathOf(state, step.Arg(0))).Count);
                    return true;

                case "expect_table_row":
                {
                    var table = Table(state, step.Arg(0));
                    var row = tables.StoredTableRowsOf(table.Path)[int.Parse(step.Arg(1), CultureInfo.InvariantCulture)];

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

        private static ScriptedSessionServer.TableAnswer SessionAnswer(string outcome)
        {
            switch (outcome)
            {
                case "stored": return ScriptedSessionServer.TableAnswer.Store();
                case "duplicate": return ScriptedSessionServer.TableAnswer.WithText("{\"ack\":{id},\"status\":200,\"body\":{\"outcome\":\"duplicate\",\"tables\":1,\"refusedKnown\":false}}");
                case "all_refused": return ScriptedSessionServer.TableAnswer.RefuseAll();
                case "invalid": return ScriptedSessionServer.TableAnswer.WithStatus(400);
                case "not_delivered": return ScriptedSessionServer.TableAnswer.WithStatus(503, 0);
                case "store_without_ack": return ScriptedSessionServer.TableAnswer.StoreSilently();
                case "no_answer": return ScriptedSessionServer.TableAnswer.NoAnswer();
                case "close_1013": return ScriptedSessionServer.TableAnswer.CloseWith(1013, "the server holds too many table frames; try again later");
                case "drop_session": return ScriptedSessionServer.TableAnswer.DropSession();
                case "binary_frame": return ScriptedSessionServer.TableAnswer.BinaryFrame();
                case "garbage_text": return ScriptedSessionServer.TableAnswer.WithText("this is not json");
                default: throw new InvalidOperationException($"Unknown block answer '{outcome}' on a session.");
            }
        }
    }
}
