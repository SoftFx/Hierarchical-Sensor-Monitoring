using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMSensorDataObjects.HistoryRequests;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.IntegrationTests.Tests
{
    /// <summary>
    /// Table sensors of the native C++ collector end to end against the real server (#1848 step 6). The native
    /// driver <c>src/native/collector/tests/server_e2e/hsm_table_e2e.cpp</c> runs the collectors (the C++ API of
    /// <c>tables.hpp</c>, the session transport with TLS to the server's self-signed certificate); this test tells it
    /// what to do, waits for states (the sensor registered, the rows stored) and reads the rows back through
    /// <c>POST api/sensors/tableHistory</c>, in CSV and JSON, comparing them exactly with the rows sent.
    ///
    /// The driver is built from the branch's source in a Debian 13 container with the distribution's libcurl, zstd
    /// and zlib (the Linux agent's stack; <c>tests/server_e2e/Dockerfile</c>) and runs on the fixture's network,
    /// reaching the server through the proxy, so a cut of the proxy cuts its connections. With
    /// <c>HSM_NATIVE_TABLE_E2E_BIN</c> set to a built driver (a Windows build, say) the test runs that one on this
    /// machine instead, against the proxy's mapped port.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class NativeTableSensorEndToEndTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan BuildPatience = TimeSpan.FromMinutes(20);

        private const string Columns = "c0:double,c1:int64";
        private const string SessionTransportReason = "table sensors need the collector session transport (hsm_collector_use_session_transport)";

        private static readonly SemaphoreSlim BuildLock = new(1, 1);
        private static string? _image;

        private readonly HsmServerFixture _fixture;
        private readonly ITestOutputHelper _output;

        public NativeTableSensorEndToEndTests(HsmServerFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task Rows_of_a_native_table_sensor_are_stored_and_read_back_across_a_lost_connection()
        {
            const int RowsAtTick = 300;
            const int RowsWhileDown = 200;
            const int RowsAtStop = 100;
            const int Total = RowsAtTick + RowsWhileDown + RowsAtStop;

            var path = $"tables/{Guid.NewGuid():N}/native";
            var baseNs = BaseNanoseconds();

            await using var driver = await NativeDriver.StartAsync(this);

            await driver.CommandAsync("collector a session native-e2e");
            Assert.Equal($"= ok {path}", await driver.CommandAsync($"table a t {path} {Columns}"));
            await driver.CommandAsync("start a");
            await UntilAsync(async () => (await driver.StatsAsync("t")).State != "Pending", "the table sensor left Pending");
            Assert.Equal("Registered", (await driver.StatsAsync("t")).State);

            // The first rows, one AddRow each, go out with a tick on the table connection.
            Assert.Equal($"= ok {RowsAtTick}", await driver.CommandAsync($"rows t {baseNs} 0 {RowsAtTick}"));
            await UntilAsync(async () => await StoredRowsAsync(path, baseNs) == RowsAtTick, $"{RowsAtTick} rows stored");

            // The connection is lost: the rows added meanwhile are kept and delivered once it is back, each once.
            var sessionsEnded = driver.LogCount("The collector session ended");
            await _fixture.DisableConnectionAsync();
            try
            {
                await UntilAsync(() => Task.FromResult(driver.LogCount("The collector session ended") > sessionsEnded), "the collector saw its session end");
                Assert.Equal($"= ok {RowsWhileDown}", await driver.CommandAsync($"batch t {baseNs} {RowsAtTick} {RowsWhileDown}"));
            }
            finally
            {
                await _fixture.EnableConnectionAsync();
            }

            await UntilAsync(async () => await StoredRowsAsync(path, baseNs) == RowsAtTick + RowsWhileDown, $"{RowsAtTick + RowsWhileDown} rows stored after the connection came back");

            // The rest is still buffered when the collector stops: the stop drain delivers it.
            Assert.Equal($"= ok {RowsAtStop}", await driver.CommandAsync($"rows t {baseNs} {RowsAtTick + RowsWhileDown} {RowsAtStop}"));
            await driver.CommandAsync("stop a");

            var stats = await driver.StatsAsync("t");
            Assert.Equal(("Registered", Total, 0L), (stats.State, stats.Accepted, stats.Dropped));

            await AssertRowsAsync(path, baseNs, Total);
        }

        [Fact]
        public async Task A_native_table_sensor_with_other_columns_is_refused_and_one_on_http_is_unavailable()
        {
            var path = $"tables/{Guid.NewGuid():N}/native";
            var baseNs = BaseNanoseconds();

            await using var driver = await NativeDriver.StartAsync(this);

            await driver.CommandAsync("collector a session native-e2e-first");
            await driver.CommandAsync($"table a t {path} {Columns}");
            await driver.CommandAsync("start a");
            await UntilAsync(async () => (await driver.StatsAsync("t")).State != "Pending", "the first table sensor left Pending");
            Assert.Equal("Registered", (await driver.StatsAsync("t")).State);
            await driver.CommandAsync("stop a");

            // Another collector registers the same path with other columns: the server refuses it, for good.
            await driver.CommandAsync("collector b session native-e2e-second");
            await driver.CommandAsync($"table b r {path} c0:double");
            await driver.CommandAsync("start b");
            await UntilAsync(async () => (await driver.StatsAsync("r")).State != "Pending", "the second table sensor left Pending");

            var refused = await driver.StatsAsync("r");
            Assert.Equal("Refused", refused.State);
            Assert.Equal($"Table sensor '{path}' is registered with the schema (time Time, c0 Double, c1 Int64); " +
                         "a registration with (time Time, c0 Double) is refused: a different shape is a different sensor.", refused.Reason);
            Assert.Contains(driver.Lines, line => line.StartsWith($"log b error The server refused the table sensor {path}: ", StringComparison.Ordinal));

            Assert.Equal("= ok 0", await driver.CommandAsync($"rows r {baseNs} 0 3"));

            refused = await driver.StatsAsync("r");
            Assert.Equal(("Refused", 0L, 3L), (refused.State, refused.Accepted, refused.Dropped));
            await driver.CommandAsync("stop b");

            // A collector on HTTP cannot send tables: Unavailable from the creation, its rows dropped and counted.
            var httpPath = $"tables/{Guid.NewGuid():N}/http";

            await driver.CommandAsync("collector h http native-e2e-http");
            await driver.CommandAsync($"table h u {httpPath} {Columns}");
            var unavailable = await driver.StatsAsync("u");
            Assert.Equal(("Unavailable", SessionTransportReason), (unavailable.State, unavailable.Reason));
            await driver.CommandAsync("start h");
            Assert.Equal("= ok 0", await driver.CommandAsync($"rows u {baseNs} 0 2"));

            unavailable = await driver.StatsAsync("u");
            Assert.Equal(("Unavailable", 0L, 2L, SessionTransportReason), (unavailable.State, unavailable.Accepted, unavailable.Dropped, unavailable.Reason));
            await driver.CommandAsync("stop h");

            // The refused registration took nothing away: the path still has the first sensor's columns and no row.
            var (status, _, body) = await ReadAsync(path, baseNs, TableHistoryFormat.Csv);
            Assert.True(status == HttpStatusCode.OK, $"tableHistory answered {(int)status}: {body}");
            Assert.Equal("time,c0,c1\r\n", body);
        }

        // The rows of the driver (hsm_table_e2e.cpp computes the same): row r has the time base + r ms
        // + (r % 7) * 100 ns + 37 ns, kept to 100 ns (rounded down); its cell of value column c is empty when
        // (r + c) % 7 == 3, else c + 1.0841 + r / 100000.0 (double) or r * 1000003 - 7 + c (int64).
        private static long RowTicks(long baseNs, long row) => DateTime.UnixEpoch.Ticks + (baseNs + row * 1_000_000 + row % 7 * 100 + 37) / 100;

        private static bool IsNull(long row, int column) => (row + column) % 7 == 3;

        private static double DoubleCell(long row) => (0 + 1.0841) + row / 100000.0;

        private static long Int64Cell(long row) => row * 1000003 - 7 + 1;

        private static string Time(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

        // Whole seconds, ten minutes ago.
        private static long BaseNanoseconds() => DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds() * 1_000_000_000;

        private async Task AssertRowsAsync(string path, long baseNs, int total)
        {
            var (status, headers, csv) = await ReadAsync(path, baseNs, TableHistoryFormat.Csv);

            Assert.True(status == HttpStatusCode.OK, $"tableHistory answered {(int)status}: {csv}");
            Assert.Equal(total.ToString(CultureInfo.InvariantCulture), headers.GetValues("X-Hsm-Row-Count").Single());
            Assert.Equal("false", headers.GetValues("X-Hsm-Truncated").Single());
            Assert.Equal("0", headers.GetValues("X-Hsm-Blocks-Failed").Single());
            Assert.Equal("0", headers.GetValues("X-Hsm-Batches-Failed").Single());

            var expected = new StringBuilder("time,c0,c1\r\n");
            for (var row = 0; row < total; row++)
            {
                expected.Append(Time(RowTicks(baseNs, row))).Append(',');
                if (!IsNull(row, 0))
                    expected.Append(DoubleCell(row).ToString("R", CultureInfo.InvariantCulture));
                expected.Append(',');
                if (!IsNull(row, 1))
                    expected.Append(Int64Cell(row).ToString(CultureInfo.InvariantCulture));
                expected.Append("\r\n");
            }

            var expectedLines = expected.ToString().Split("\r\n");
            var actualLines = csv.Split("\r\n");
            var firstDifference = Enumerable.Range(0, Math.Min(expectedLines.Length, actualLines.Length)).FirstOrDefault(i => expectedLines[i] != actualLines[i], -1);

            Assert.True(expectedLines.Length == actualLines.Length && firstDifference < 0,
                $"CSV: {actualLines.Length} lines, {expectedLines.Length} expected; first difference at line {firstDifference}: " +
                (firstDifference < 0 ? "" : $"'{actualLines[firstDifference]}', expected '{expectedLines[firstDifference]}'"));

            (status, _, var json) = await ReadAsync(path, baseNs, TableHistoryFormat.Json);

            Assert.True(status == HttpStatusCode.OK, $"tableHistory answered {(int)status}: {json}");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(new[] { "time", "c0", "c1" }, root.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
            Assert.Equal(new[] { "time", "double", "int64" }, root.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
            Assert.Equal(total, root.GetProperty("rowCount").GetInt32());

            var rows = root.GetProperty("rows").EnumerateArray().ToList();

            Assert.Equal(total, rows.Count);

            for (var row = 0; row < total; row++)
            {
                var cells = rows[row].EnumerateArray().ToList();

                Assert.Equal(Time(RowTicks(baseNs, row)), cells[0].GetString());

                if (IsNull(row, 0))
                    Assert.Equal(JsonValueKind.Null, cells[1].ValueKind);
                else
                    Assert.Equal(BitConverter.DoubleToInt64Bits(DoubleCell(row)), BitConverter.DoubleToInt64Bits(cells[1].GetDouble()));

                if (IsNull(row, 1))
                    Assert.Equal(JsonValueKind.Null, cells[2].ValueKind);
                else
                    Assert.Equal(Int64Cell(row), cells[2].GetInt64());
            }
        }

        private async Task<int> StoredRowsAsync(string path, long baseNs)
        {
            var (status, headers, _) = await ReadAsync(path, baseNs, TableHistoryFormat.Csv);

            return status == HttpStatusCode.OK ? int.Parse(headers.GetValues("X-Hsm-Row-Count").Single(), CultureInfo.InvariantCulture) : -1;
        }

        private async Task<(HttpStatusCode Status, System.Net.Http.Headers.HttpResponseHeaders Headers, string Body)> ReadAsync(string path, long baseNs, TableHistoryFormat format)
        {
            var from = new DateTime(RowTicks(baseNs, 0), DateTimeKind.Utc).AddSeconds(-1);
            var request = new TableHistoryRequest { Path = path, From = from, To = from.AddHours(1), Format = format };

            using var client = _fixture.CreateSensorApiClient();
            using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/sensors/tableHistory", content);

            return (response.StatusCode, response.Headers, await response.Content.ReadAsStringAsync());
        }

        private static async Task UntilAsync(Func<Task<bool>> condition, string what)
        {
            var clock = Stopwatch.StartNew();

            while (!await condition())
            {
                Assert.True(clock.Elapsed < Patience, $"Not within {Patience.TotalSeconds} s: {what}.");
                await Task.Delay(100);
            }
        }

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "native", "collector", "tests", "server_e2e", "Dockerfile")))
                    return directory.FullName;

            throw new InvalidOperationException($"No repository root above {AppContext.BaseDirectory}.");
        }

        // The driver's image, built once per test run from the branch's source.
        private async Task<string> ImageAsync()
        {
            await BuildLock.WaitAsync();
            try
            {
                if (_image != null)
                    return _image;

                var collector = Path.Combine(RepositoryRoot(), "src", "native", "collector");
                // One name, as the fixture's test image: a rebuild replaces it instead of adding one per run.
                const string image = "hsm-native-table-e2e:latest";
                var (exit, output) = await RunAsync("docker", new[] { "build", "-f", Path.Combine(collector, "tests", "server_e2e", "Dockerfile"), "-t", image, collector }, BuildPatience);

                Assert.True(exit == 0, $"docker build of the native driver failed ({exit}):\n{output}");

                _image = image;
                return image;
            }
            finally
            {
                BuildLock.Release();
            }
        }

        private static async Task<(int Exit, string Output)> RunAsync(string file, IEnumerable<string> arguments, TimeSpan patience)
        {
            var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start.");
            var output = new StringBuilder();
            var stdout = Task.Run(async () => output.Append(await process.StandardOutput.ReadToEndAsync()));
            var stderr = Task.Run(async () => output.Append(await process.StandardError.ReadToEndAsync()));

            using var timeout = new CancellationTokenSource(patience);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return (-1, $"{file} did not finish within {patience}");
            }

            await Task.WhenAll(stdout, stderr);
            return (process.ExitCode, output.ToString());
        }

        private sealed record Stats(string State, long Accepted, long Dropped, string Reason);

        // One run of hsm_table_e2e: commands on its stdin, an answer line ("= ...") for each, and its collectors'
        // log lines ("log <collector> <level> <text>") in between.
        private sealed class NativeDriver : IAsyncDisposable
        {
            private readonly Process _process;
            private readonly ITestOutputHelper _output;
            private readonly string? _container;
            private readonly BlockingCollection<string> _answers = new();
            private readonly ConcurrentQueue<string> _lines = new();
            private readonly Task _reader;

            private NativeDriver(Process process, ITestOutputHelper output, string? container)
            {
                _process = process;
                _output = output;
                _container = container;
                _reader = Task.Run(ReadLinesAsync);
            }

            public IEnumerable<string> Lines => _lines;

            public int LogCount(string text) => _lines.Count(line => line.StartsWith("log ", StringComparison.Ordinal) && line.Contains(text, StringComparison.Ordinal));

            public static async Task<NativeDriver> StartAsync(NativeTableSensorEndToEndTests test)
            {
                var fixture = test._fixture;
                var binary = Environment.GetEnvironmentVariable("HSM_NATIVE_TABLE_E2E_BIN");
                ProcessStartInfo start;
                string? container = null;

                if (!string.IsNullOrEmpty(binary))
                {
                    start = new ProcessStartInfo(binary);
                    start.ArgumentList.Add(fixture.ServerAddress);
                    start.ArgumentList.Add(fixture.MappedSensorPort.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    var image = await test.ImageAsync();

                    container = $"hsm-native-table-e2e-{Guid.NewGuid():N}";
                    start = new ProcessStartInfo("docker");
                    foreach (var argument in new[] { "run", "--rm", "-i", "--name", container, "--network", fixture.NetworkName, "-e", "HSM_E2E_KEY", image,
                                                     HsmServerFixture.ProxyHost, HsmServerFixture.ProxyPort.ToString(CultureInfo.InvariantCulture) })
                        start.ArgumentList.Add(argument);
                }

                // The key goes through the environment, not the command line.
                start.Environment["HSM_E2E_KEY"] = fixture.AccessKey;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.UseShellExecute = false;

                var process = Process.Start(start) ?? throw new InvalidOperationException("The native driver did not start.");
                var driver = new NativeDriver(process, test._output, container);

                Assert.StartsWith("= ready ", driver.Answer("start"));
                return driver;
            }

            public async Task<string> CommandAsync(string command)
            {
                await _process.StandardInput.WriteLineAsync(command);
                await _process.StandardInput.FlushAsync();

                var answer = Answer(command);

                Assert.True(answer.StartsWith("= ok", StringComparison.Ordinal), $"The driver answered '{command}' with '{answer}'.");
                return answer;
            }

            public async Task<Stats> StatsAsync(string table)
            {
                var words = (await CommandAsync($"stats {table}")).Split(' ', 6);

                return new Stats(words[2], long.Parse(words[3], CultureInfo.InvariantCulture), long.Parse(words[4], CultureInfo.InvariantCulture),
                                 words.Length > 5 ? words[5] : "");
            }

            private string Answer(string command)
            {
                Assert.True(_answers.TryTake(out var answer, Patience), $"The driver gave no answer to '{command}' within {Patience.TotalSeconds} s.");
                return answer;
            }

            private async Task ReadLinesAsync()
            {
                var stderr = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await _process.StandardError.ReadLineAsync()) != null)
                        _lines.Enqueue("stderr " + line);
                });

                string? text;
                while ((text = await _process.StandardOutput.ReadLineAsync()) != null)
                {
                    _lines.Enqueue(text);
                    if (text.StartsWith("= ", StringComparison.Ordinal))
                        _answers.Add(text);
                }

                await stderr;
                _answers.CompleteAdding();
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("quit");
                    await _process.StandardInput.FlushAsync();
                }
                catch (IOException)
                {
                    // It has ended already.
                }

                using (var timeout = new CancellationTokenSource(Patience))
                {
                    try
                    {
                        await _process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }

                if (_container != null)
                    await RunAsync("docker", new[] { "rm", "-f", _container }, Patience);

                await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(5)));

                // The whole conversation, for a failed run.
                foreach (var line in _lines)
                    _output.WriteLine(line);

                _process.Dispose();
                _answers.Dispose();
            }
        }
    }
}
