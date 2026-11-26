using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tests.LargeFiles;
using HSMDataCollector.Tests.Transport;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// The large file verbs of the conformance corpus (#1888, <c>tests/conformance/collector/large_file_contract.hsmtest</c>):
    /// a collector on the scripted session sends files the driver writes into the case's folder (seeded content both drivers
    /// reproduce), against the scripted server's file connection. Time is the case's: the uploader's clock moves only when a
    /// verb moves it (a second at a time, every second a scan) or when the uploader waits a server's retryAfter, which passes
    /// at once. Each case has its own folders, so a restart finds the state of that case only.
    /// </summary>
    public sealed partial class CollectorConformanceTests
    {
        private static readonly DateTime LargeFileTime = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        private sealed partial class ContractState
        {
            public ContractStep SessionStep { get; set; }

            public string LargeFileRoot { get; set; }

            public VirtualTimings LargeFileTimings { get; set; }

            public Dictionary<string, Task<LargeFileResult>> HandedFiles { get; } = new Dictionary<string, Task<LargeFileResult>>(StringComparer.Ordinal);

            public List<ILargeFileWatch> Watches { get; } = new List<ILargeFileWatch>();

            public string LargeFileFolder => Path.Combine(LargeFileRoot, "files");
        }

        // Every collector of a case gets the case's data folder and the case's clock.
        private static void ConfigureLargeFiles(ContractState state, CollectorOptions options)
        {
            EnsureLargeFileRoot(state);

            state.LargeFileTimings = new VirtualTimings();
            options.DataFolder = Path.Combine(state.LargeFileRoot, "data");
            options.LargeFileTimings = state.LargeFileTimings;
        }

        private static void EnsureLargeFileRoot(ContractState state)
        {
            if (state.LargeFileRoot != null)
                return;

            state.LargeFileRoot = Path.Combine(Path.GetTempPath(), "hsm-conformance-large-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(state.LargeFileRoot);
            Directory.CreateDirectory(state.LargeFileFolder);
        }

        private static void CleanUpLargeFiles(ContractState state)
        {
            if (state.LargeFileRoot == null)
                return;

            try { Directory.Delete(state.LargeFileRoot, true); }
            catch (Exception) { }
        }

        private static ScriptedSessionServer FileServer(ContractState state, string verb) =>
            state.SessionServer ?? throw new InvalidOperationException($"{verb} needs the scripted session server (create_collector_on_session).");

        private static LargeFileService LargeFilesOf(ContractState state) => state.Collector.LargeFileService;

        private static FileAnswer ParseFileAnswer(string text)
        {
            var parts = text.Split(':');

            switch (parts[0])
            {
                case "stored": return FileAnswer.Stored();
                case "checking": return FileAnswer.Checking(1);
                case "no_room": return FileAnswer.NoRoom(1);
                case "busy": return FileAnswer.Busy(1);
                case "too_large": return FileAnswer.TooLarge();
                case "cannot_take": return FileAnswer.CannotTake();
                case "invalid": return FileAnswer.Invalid();
                case "checksum_mismatch": return FileAnswer.With(409, $"{{\"next\":{(parts.Length > 1 ? parts[1] : "0")},\"reason\":\"checksum mismatch\"}}");
                case "out_of_order": return FileAnswer.With(409, $"{{\"next\":{parts[1]},\"reason\":\"out of order\"}}");
                case "missing": return FileAnswer.With(409, $"{{\"next\":{parts[1]},\"reason\":\"pieces are missing\"}}");
                case "fault": return FileAnswer.With(503, null, 1);
                case "discarded":
                case "mismatch": return FileAnswer.Discarded();
                case "drop": return FileAnswer.Drop();
                case "drop_after_store": return FileAnswer.Drop(applied: true);
                case "silent": return FileAnswer.Silent();
                case "close": return FileAnswer.Close(int.Parse(parts[1], CultureInfo.InvariantCulture));
                default: throw new InvalidOperationException($"Unknown file server answer '{text}'.");
            }
        }

        private static LargeFileOutcome ParseOutcome(string text)
        {
            switch (text)
            {
                case "stored": return LargeFileOutcome.Stored;
                case "refused": return LargeFileOutcome.Refused;
                case "given_up": return LargeFileOutcome.GivenUp;
                case "missing": return LargeFileOutcome.Missing;
                case "unavailable": return LargeFileOutcome.Unavailable;
                case "cancelled": return LargeFileOutcome.Cancelled;
                default: throw new InvalidOperationException($"Unknown large file outcome '{text}'.");
            }
        }

        private static LargeFileOptions LargeFileOptionsOf(string keepOrDelete, TimeSpan? settle = null)
        {
            var options = new LargeFileOptions { DeleteAfterUpload = keepOrDelete != "keep" };

            if (settle.HasValue)
                options.SettleTime = settle.Value;

            return options;
        }

        // The case's clock a second forward, then the uploader's look at the world after it (a handshake, not a sleep: the
        // case never outruns the scans it relies on).
        private static async Task TickLargeFileClockAsync(ContractState state)
        {
            var service = state.Collector == null ? null : LargeFilesOf(state);

            if (state.LargeFileTimings == null || service == null)
            {
                state.LargeFileTimings?.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(5).ConfigureAwait(false);
                return;
            }

            await Task.Run(() => state.LargeFileTimings.Step(service.Wake)).ConfigureAwait(false);
        }

        // Polls; between polls the case's clock moves a second, so a wait the uploader set (a retry, a file put back) passes.
        private static async Task<bool> PollAdvancingAsync(ContractState state, Func<bool> condition, string timeoutSeconds)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(int.Parse(timeoutSeconds, CultureInfo.InvariantCulture));

            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                    return false;

                await TickLargeFileClockAsync(state).ConfigureAwait(false);
            }

            return true;
        }

        private static async Task<bool> ExecuteLargeFileStepAsync(ContractState state, ContractStep step)
        {
            switch (step.Action)
            {
                // ---- sources ----

                case "large_file_create":
                {
                    // name|size|seed[|modified]: xorshift32 content (#1888 design §12.10), modified 2026-11-25T10:00:00Z by default.
                    EnsureLargeFileRoot(state);

                    var path = Path.Combine(state.LargeFileFolder, step.Arg(0));
                    var modified = step.TryArg(3, out var text) ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) : LargeFileTime;

                    File.WriteAllBytes(path, SeededContent.Make(long.Parse(step.Arg(1), CultureInfo.InvariantCulture), uint.Parse(step.Arg(2), CultureInfo.InvariantCulture)));
                    File.SetLastWriteTimeUtc(path, modified);
                    return true;
                }

                case "large_file_change_after_piece":
                {
                    // name|piece: once piece `piece` of the file has been answered, its modification time is an hour later (a file
                    // that changed while it was sent). Windows denies other writers while the collector holds the file, so the
                    // managed driver changes what the collector reads of it.
                    var name = step.Arg(0);
                    var piece = long.Parse(step.Arg(1), CultureInfo.InvariantCulture);
                    var changed = 0;
                    var server = FileServer(state, step.Action);

                    server.Files.AfterPiece = number => { if (number == piece) Interlocked.Exchange(ref changed, 1); };
                    state.LargeFileTimings.FactsHook = (path, facts) =>
                        Volatile.Read(ref changed) == 1 && Path.GetFileName(path) == name
                            ? new FileFacts(facts.Name, facts.Size, facts.ModifiedUtc.AddHours(1), facts.Source)
                            : facts;
                    return true;
                }

                case "large_file_send":
                {
                    // sensor|name|keep_or_delete
                    EnsureLargeFileRoot(state);
                    state.HandedFiles[step.Arg(1)] = state.Collector.SendLargeFileAsync(step.Arg(0), Path.Combine(state.LargeFileFolder, step.Arg(1)), LargeFileOptionsOf(step.Arg(2)));
                    return true;
                }

                case "large_file_send_not_secure":
                {
                    // sensor|name|keep_or_delete, with LargeFileOptions.Secure off.
                    EnsureLargeFileRoot(state);

                    var options = LargeFileOptionsOf(step.Arg(2));

                    options.Secure = false;
                    state.HandedFiles[step.Arg(1)] = state.Collector.SendLargeFileAsync(step.Arg(0), Path.Combine(state.LargeFileFolder, step.Arg(1)), options);
                    return true;
                }

                case "large_file_watch":
                {
                    // sensor|mask|settle_s|keep_or_delete, on the case's folder.
                    EnsureLargeFileRoot(state);
                    state.Watches.Add(state.Collector.WatchLargeFiles(step.Arg(0), state.LargeFileFolder, step.Arg(1),
                                                                      LargeFileOptionsOf(step.Arg(3), TimeSpan.FromSeconds(int.Parse(step.Arg(2), CultureInfo.InvariantCulture)))));
                    return true;
                }

                case "large_file_clock_advance":
                {
                    // seconds: the case's clock moves a second at a time, a scan after every second.
                    var seconds = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);

                    for (var i = 0; i < seconds; i++)
                        await TickLargeFileClockAsync(state).ConfigureAwait(false);

                    return true;
                }

                case "large_file_restart":
                {
                    // Stop and dispose the collector, then a new one on the same server and the same data folder.
                    await state.Collector.Stop().ConfigureAwait(false);
                    state.Collector.Dispose();
                    state.HandedFiles.Clear();
                    state.Watches.Clear();

                    var args = state.SessionStep;

                    state.Collector = CreateCollectorOnSession(SessionServer(state), int.Parse(args.Arg(0)), int.Parse(args.Arg(1)), int.Parse(args.Arg(2)),
                                                               state.Directives, options => ConfigureLargeFiles(state, options));
                    ConfigureFileConnection(state);
                    await state.Collector.Start().ConfigureAwait(false);
                    return true;
                }

                case "create_collector_on_http":
                {
                    // A collector whose transport is HTTP to an address nothing listens on: large files are unavailable on it.
                    EnsureLargeFileRoot(state);
                    state.Collector = new DataCollector(new CollectorOptions
                    {
                        AccessKey = "conformance-key",
                        ServerAddress = "https://localhost",
                        Port = 1,
                        Transport = CollectorTransport.Http,
                        ComputerName = "conformance-host",
                        Module = "conformance-module",
                        DataFolder = Path.Combine(state.LargeFileRoot, "data"),
                    });
                    return true;
                }

                // ---- the server's script ----

                case "file_server_refuse_upgrades":
                    FileServer(state, step.Action).RefuseFileUpgrades(int.Parse(step.Arg(0), CultureInfo.InvariantCulture));
                    return true;

                case "file_server_accept_upgrades":
                    FileServer(state, step.Action).AcceptFileUpgrades();
                    return true;

                case "file_server_limits":
                    // piece_bytes|max_file_bytes: what the file hello states from now on.
                    FileServer(state, step.Action).Files.PieceBytes = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);
                    FileServer(state, step.Action).Files.MaxFileBytes = long.Parse(step.Arg(1), CultureInfo.InvariantCulture);
                    return true;

                case "file_server_holds":
                {
                    // name|pieces: the server already has the first `pieces` pieces of that file.
                    var path = Path.Combine(state.LargeFileFolder, step.Arg(0));

                    FileServer(state, step.Action).Files.Holds(step.Arg(0), File.ReadAllBytes(path), long.Parse(step.Arg(1), CultureInfo.InvariantCulture));
                    return true;
                }

                case "file_server_answer_begin_next":
                    FileServer(state, step.Action).Files.AnswerBeginNext(ParseFileAnswer(step.Arg(0)));
                    return true;

                case "file_server_answer_piece_next":
                    FileServer(state, step.Action).Files.AnswerPieceNext(step.Arg(0) == "real" ? null : ParseFileAnswer(step.Arg(0)));
                    return true;

                case "file_server_answer_end_next":
                    FileServer(state, step.Action).Files.AnswerEndNext(ParseFileAnswer(step.Arg(0)));
                    return true;

                case "file_server_answer_discard_next":
                    FileServer(state, step.Action).Files.AnswerDiscardNext(ParseFileAnswer(step.Arg(0)));
                    return true;

                // ---- expectations ----

                case "expect_large_file_result":
                {
                    // name|outcome|timeout_s
                    Assert.True(state.HandedFiles.TryGetValue(step.Arg(0), out var task), $"expect_large_file_result: {step.Arg(0)} was not handed over.");
                    Assert.True(await PollAdvancingAsync(state, () => task.IsCompleted, step.Arg(2)).ConfigureAwait(false),
                                $"expect_large_file_result: {step.Arg(0)} did not settle; the server saw {string.Join(",", state.SessionServer?.Files.LogSnapshot() ?? new List<string>())}.");
                    Assert.Equal(ParseOutcome(step.Arg(1)), task.Result.Outcome);
                    return true;
                }

                case "expect_large_file_pending":
                {
                    // name: the hand-over has not settled.
                    Assert.True(state.HandedFiles.TryGetValue(step.Arg(0), out var task), $"expect_large_file_pending: {step.Arg(0)} was not handed over.");
                    await TickLargeFileClockAsync(state).ConfigureAwait(false);
                    Assert.False(task.IsCompleted, $"expect_large_file_pending: {step.Arg(0)} settled as {(task.IsCompleted ? task.Result.ToString() : "")}.");
                    return true;
                }

                case "expect_large_file_watch_state":
                {
                    // index|state
                    var expected = (LargeFileWatchState)Enum.Parse(typeof(LargeFileWatchState), step.Arg(1).Replace("_", string.Empty), ignoreCase: true);

                    Assert.Equal(expected, state.Watches[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)].State);
                    return true;
                }

                case "expect_large_file_source":
                {
                    // name|present_or_absent
                    var exists = File.Exists(Path.Combine(state.LargeFileFolder, step.Arg(0)));

                    Assert.True(step.Arg(1) == "present" ? exists : !exists, $"expect_large_file_source: {step.Arg(0)} is {(exists ? "present" : "absent")}.");
                    return true;
                }

                case "expect_file_stored":
                {
                    // name|seed|timeout_s: the server stored and checked the file, and its bytes are the seeded content.
                    var server = FileServer(state, step.Action);

                    Assert.True(await PollAdvancingAsync(state, () => server.Files.StoredCount(step.Arg(0)) > 0, step.Arg(2)).ConfigureAwait(false),
                                $"expect_file_stored: {step.Arg(0)} was not stored; the server saw {string.Join(",", server.Files.LogSnapshot())}.");

                    var stored = server.Files.StoredFile(step.Arg(0));

                    Assert.Equal(SeededContent.Make(stored.Length, uint.Parse(step.Arg(1), CultureInfo.InvariantCulture)), stored);
                    return true;
                }

                case "expect_file_stored_count":
                    Assert.Equal(int.Parse(step.Arg(1), CultureInfo.InvariantCulture), FileServer(state, step.Action).Files.StoredCount(step.Arg(0)));
                    return true;

                case "expect_file_begins":
                {
                    // name|count|timeout_s: begins of the file the server received (polled up to count, then equality).
                    var server = FileServer(state, step.Action);
                    var expected = int.Parse(step.Arg(1), CultureInfo.InvariantCulture);

                    await PollAdvancingAsync(state, () => server.Files.BeginCount(step.Arg(0)) >= expected, step.Arg(2)).ConfigureAwait(false);
                    Assert.Equal(expected, server.Files.BeginCount(step.Arg(0)));
                    return true;
                }

                case "expect_file_begin_order":
                {
                    // name,name,...: the first begin of every file, in order.
                    var order = FileServer(state, step.Action).Files.LogSnapshot().Where(l => l.StartsWith("begin ", StringComparison.Ordinal))
                                                                       .Select(l => l.Substring(6)).Distinct().ToList();

                    Assert.Equal(step.Arg(0).Split(','), order);
                    return true;
                }

                case "expect_file_begin_field":
                {
                    // index|field|value: a field of begin number `index` (0 = the first), as its JSON text.
                    var begin = FileServer(state, step.Action).Files.Begins[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)];
                    var field = begin.GetProperty(step.Arg(1));
                    var value = field.ValueKind == System.Text.Json.JsonValueKind.String ? field.GetString() : field.GetRawText();

                    Assert.Equal(step.Arg(2), value);
                    return true;
                }

                case "expect_file_begin_field_starts":
                {
                    var begin = FileServer(state, step.Action).Files.Begins[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)];
                    var field = begin.GetProperty(step.Arg(1));

                    Assert.StartsWith(step.Arg(2), field.ValueKind == System.Text.Json.JsonValueKind.String ? field.GetString() : field.GetRawText());
                    return true;
                }

                case "expect_file_begin_field_absent":
                {
                    var begin = FileServer(state, step.Action).Files.Begins[int.Parse(step.Arg(0), CultureInfo.InvariantCulture)];

                    Assert.False(begin.TryGetProperty(step.Arg(1), out _), $"expect_file_begin_field_absent: begin {step.Arg(0)} carries {step.Arg(1)}.");
                    return true;
                }

                case "expect_file_stored_size":
                {
                    // name|bytes|timeout_s
                    var server = FileServer(state, step.Action);

                    Assert.True(await PollAdvancingAsync(state, () => server.Files.StoredCount(step.Arg(0)) > 0, step.Arg(2)).ConfigureAwait(false),
                                $"expect_file_stored_size: {step.Arg(0)} was not stored; the server saw {string.Join(",", server.Files.LogSnapshot())}.");
                    Assert.Equal(long.Parse(step.Arg(1), CultureInfo.InvariantCulture), server.Files.StoredFile(step.Arg(0)).Length);
                    return true;
                }

                case "expect_file_pieces":
                    // n0,n1,...: the piece numbers of every piece frame received, in order of arrival.
                    Assert.Equal(step.Arg(0).Split(',').Select(n => long.Parse(n, CultureInfo.InvariantCulture)), FileServer(state, step.Action).Files.PieceNumbersSnapshot());
                    return true;

                case "expect_file_discards":
                {
                    var server = FileServer(state, step.Action);
                    var expected = int.Parse(step.Arg(0), CultureInfo.InvariantCulture);

                    await PollAdvancingAsync(state, () => server.Files.Discards >= expected, step.Arg(1)).ConfigureAwait(false);
                    Assert.Equal(expected, server.Files.Discards);
                    return true;
                }

                case "expect_file_connections_at_least":
                {
                    var server = FileServer(state, step.Action);

                    Assert.True(await PollAdvancingAsync(state, () => server.FileConnections >= int.Parse(step.Arg(0), CultureInfo.InvariantCulture), step.Arg(1)).ConfigureAwait(false),
                                $"expect_file_connections_at_least: {server.FileConnections} file connections.");
                    return true;
                }

                case "expect_refused_file_upgrades_at_least":
                {
                    var server = FileServer(state, step.Action);

                    Assert.True(await PollAdvancingAsync(state, () => server.RefusedFileUpgrades >= int.Parse(step.Arg(0), CultureInfo.InvariantCulture), step.Arg(1)).ConfigureAwait(false),
                                $"expect_refused_file_upgrades_at_least: {server.RefusedFileUpgrades} refused.");
                    return true;
                }

                case "expect_large_files_not_sent":
                {
                    // count: files not sent, as counted in the watches and hand-overs (refused, given up, missing).
                    Assert.Equal(int.Parse(step.Arg(0), CultureInfo.InvariantCulture), state.HandedFiles.Values.Count(t => t.IsCompleted && t.Result.Outcome != LargeFileOutcome.Stored)
                                                                                    + (int)state.Watches.Sum(w => w.FilesNotSent));
                    return true;
                }

                default:
                    return false;
            }
        }

        // The ack wait of a request the scripted server leaves unanswered ends when it decides so, not by a clock.
        private static void ConfigureFileConnection(ContractState state)
        {
            if (state.SessionServer == null || !(OptionsOf(state.Collector).DataSender is HSMDataCollector.Client.Session.SessionDataSender sender))
                return;

            var server = state.SessionServer;

            sender.Files.AckDeadlineForTests = id => server.WhenFileRequestUnanswered(id);
        }
    }
}
