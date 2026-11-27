using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.LargeFiles;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using Xunit;
using Xunit.Abstractions;

namespace HSMServer.Core.Tests.LargeFiles
{
    /// <summary>Runs only when HSM_LARGE_FILE_START_CHECK_BENCH=1; skipped otherwise (and so in CI).</summary>
    public sealed class LargeFileStartCheckBenchmarkFactAttribute : FactAttribute
    {
        public LargeFileStartCheckBenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HSM_LARGE_FILE_START_CHECK_BENCH") != "1")
                Skip = "benchmark: set HSM_LARGE_FILE_START_CHECK_BENCH=1 to run";
        }
    }


    /// <summary>
    /// The cost of the check of the values at start (#1913): a server that held a large file, with a history of N ordinary
    /// file values of 64 KiB each (random, so incompressible) in four File sensors over four weeks. The databases are
    /// written, closed and opened again (LevelDB's block cache is cold; the operating system's file cache is not), then
    /// the start pass runs three times. Measured per run: the managed bytes the process allocated, the bytes it read
    /// through ReadFile (LevelDB maps its table files on Windows, so this is usually near zero), the CPU time and the wall
    /// time. <c>HSM_LARGE_FILE_START_CHECK_BENCH=1 dotnet test --filter "FullyQualifiedName~LargeFileStartCheckBenchmark"
    /// --logger "console;verbosity=detailed"</c> (HSM_LARGE_FILE_START_CHECK_BENCH_VALUES changes N, default 20,000).
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class LargeFileStartCheckBenchmark(ITestOutputHelper output) : IDisposable
    {
        private const int ValueSize = 64 * 1024;
        private const int Sensors = 4;
        private const int Piece = 65_536;

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-lf-start-bench-{Guid.NewGuid():N}");


        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        [LargeFileStartCheckBenchmarkFact]
        public async Task The_start_check_over_a_history_of_ordinary_file_values()
        {
            var values = int.TryParse(Environment.GetEnvironmentVariable("HSM_LARGE_FILE_START_CHECK_BENCH_VALUES"), out var n) ? n : 20_000;
            var product = TestProductsManager.ProductId;
            var settings = new HSMDatabase.Settings.DatabaseSettings
            {
                DatabaseFolder = Path.Combine(_root, "Databases"),
                LargeFilesFolder = Path.Combine(_root, "LargeFiles"),
            };

            var first = new HSMDatabase.DatabaseWorkCore.DatabaseCore(settings);
            first.AddProduct(TestProductsManager.TestProduct);
            first.Dispose();

            var (database, cache) = Restart(settings);
            var service = new LargeFileUploadService(database, cache, new LargeFileOptions { MinFreeDiskMb = 0 });
            var sensorIds = new Guid[Sensors];

            for (var s = 0; s < Sensors; s++)
            {
                var path = $"bench/f{s}";
                Assert.True((await cache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, path)
                {
                    Update = new SensorUpdate { Id = Guid.Empty },
                    Type = SensorType.File,
                    TypeIsExplicit = true,
                })).IsOk);
                Assert.True(cache.TryGetSensorByPath(product, path, out var sensor));
                sensorIds[s] = sensor.Id;
            }

            // The store has held a file: the check runs.
            var content = new byte[Piece];
            new Random(7).NextBytes(content);
            var begin = await service.BeginAsync(new LargeFileBeginRequest(product, "bench/f0", "a.dmp", content.Length, DateTime.UtcNow, null, Piece, Piece), default);
            await service.AppendAsync(begin.Lease, 0, content, SHA256.HashData(content), default);
            Assert.Equal(LargeFileFinishOutcome.Verified, (await service.FinishAsync(begin.Lease, SHA256.HashData(content), new StoredBytesHashChecker(), default)).Outcome);

            var random = new Random(1);
            var start = DateTime.UtcNow.AddDays(-28);
            var step = TimeSpan.FromDays(27).Ticks / values;
            var write = Stopwatch.StartNew();

            for (var i = 0; i < values; i++)
            {
                var bytes = new byte[ValueSize];
                random.NextBytes(bytes);

                var time = start.AddTicks(step * i);
                database.AddSensorValue(sensorIds[i % Sensors], new FileValue { Time = time, ReceivingTime = time, Value = bytes, OriginalSize = ValueSize, Name = $"v{i}", Extension = "log" });
            }

            output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} cores");
            output.WriteLine($"history: {values:N0} ordinary file values of {ValueSize / 1024} KiB in {Sensors} File sensors over 27 days ({(long)values * ValueSize / 1048576.0:N0} MiB of content), written in {write.Elapsed.TotalSeconds:F1} s");

            await cache.DisposeAsync();
            database.Dispose();

            var (restarted, restartedCache) = Restart(database.DatabaseSettings);
            await using var _ = restartedCache;
            var restartedService = new LargeFileUploadService(restarted, restartedCache, new LargeFileOptions { MinFreeDiskMb = 0 });

            for (var run = 1; run <= 3; run++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var process = Process.GetCurrentProcess();
                var allocated = GC.GetTotalAllocatedBytes(precise: true);
                var read = ReadTransferBytes();
                var cpu = process.TotalProcessorTime;
                var wall = Stopwatch.StartNew();

                var report = await restartedService.RunPassAsync(DateTime.UtcNow, atStart: true);

                wall.Stop();
                process.Refresh();

                output.WriteLine($"run {run}: allocated {(GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1048576.0:N1} MiB, read through ReadFile {(ReadTransferBytes() - read) / 1048576.0:N1} MiB, " +
                                 $"CPU {(process.TotalProcessorTime - cpu).TotalMilliseconds:N0} ms, wall {wall.Elapsed.TotalMilliseconds:N0} ms, large values without a record {report.RowsWithoutRecord}");
            }

            restarted.Dispose();
        }

        private static (HSMDatabase.DatabaseWorkCore.DatabaseCore Database, TreeValuesCache Cache) Restart(HSMDatabase.AccessManager.IDatabaseSettings settings)
        {
            var restart = ((HSMDatabase.Settings.DatabaseSettings)settings) with
            {
                ServerLayoutDatabaseName = settings.ServerLayoutDatabaseName + "_restart",
                ChangeSetsDatabaseName = settings.ChangeSetsDatabaseName + "_restart",
                SnaphotsDatabaseName = settings.SnaphotsDatabaseName + "_restart",
            };
            var database = new HSMDatabase.DatabaseWorkCore.DatabaseCore(restart);
            var snapshot = new Moq.Mock<HSMServer.Core.TreeStateSnapshot.ITreeStateSnapshot>();
            snapshot.Setup(s => s.Keys).Returns(new HSMServer.Core.TreeStateSnapshot.StateCollection<
                HSMServer.Core.TreeStateSnapshot.States.LastKeyState, HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity.LastKeyStateEntity>());

            var cache = new TreeValuesCache(database, snapshot.Object, new HSMServer.Core.Journal.JournalService(database),
                new HSMServer.Core.Schedule.AlertScheduleProvider(database), new HSMServer.Core.ChangeSets.ChangeSetJournal(database));

            return (database, cache);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

        private static long ReadTransferBytes() =>
            OperatingSystem.IsWindows() && GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var counters) ? (long)counters.ReadTransferCount : -1;
    }
}
