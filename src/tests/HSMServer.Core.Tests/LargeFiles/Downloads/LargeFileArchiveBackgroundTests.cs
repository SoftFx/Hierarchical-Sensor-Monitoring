using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.BackgroundServices;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Tests.BackgroundServices;
using HSMServer.Model.ManagementApi.LargeFiles;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The hosted service of the archive side (#1885), its self-monitoring sensors, its configuration section and the
    // name a downloaded archive is saved as.
    public sealed class LargeFileArchiveBackgroundTests : IDisposable
    {
        private readonly FakeLargeFileArchiveStore _store = new();
        private readonly Mock<IArchiveBuilder> _builder = new();
        private readonly Mock<IDumpPasswordService> _passwords = new();
        private readonly Mock<IDownloadLog> _log = new();
        private readonly Mock<ILargeFilesMonitor> _monitor = new();
        private readonly Mock<IServerConfig> _config = new();
        private readonly ManualTime _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));


        public LargeFileArchiveBackgroundTests()
        {
            _config.Setup(c => c.LargeFileArchives).Returns(new LargeFileArchivesConfig { DownloadLogRetentionDays = 365 });
            _builder.Setup(b => b.RunDueAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        public void Dispose() => _store.Dispose();


        private LargeFileArchiveBackgroundService CreateService() =>
            new(_builder.Object, _store, _passwords.Object, _log.Object, _monitor.Object, _config.Object, _time,
                NullLogger<LargeFileArchiveBackgroundService>.Instance);


        [Fact]
        public async Task SignalsDuringARun_CoalesceIntoOneMoreRun_AndRunsNeverOverlap()
        {
            var running = 0;
            var maxRunning = 0;
            var runs = 0;
            var firstRunEntered = new TaskCompletionSource();
            var releaseFirstRun = new TaskCompletionSource();

            _builder.Setup(b => b.RunDueAsync(It.IsAny<CancellationToken>())).Returns(async () =>
            {
                var now = Interlocked.Increment(ref running);
                maxRunning = Math.Max(maxRunning, now);

                if (Interlocked.Increment(ref runs) == 1)
                {
                    firstRunEntered.SetResult();
                    await releaseFirstRun.Task;
                }

                Interlocked.Decrement(ref running);
            });

            using var service = CreateService();
            using var stop = new CancellationTokenSource();

            // The loop reports each time it goes idle for its period: the number of runs so far, and whether a wake-up is
            // still pending (a pending one would start another run at once).
            var idleAfterTwoRuns = new TaskCompletionSource<(int Runs, bool Pending)>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OnWaitingForPeriod = pending =>
            {
                var sofar = Volatile.Read(ref runs);
                if (sofar >= 2)
                    idleAfterTwoRuns.TrySetResult((sofar, pending));
            };

            await service.StartAsync(stop.Token);
            await firstRunEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Three signals while the first run works: a verified file, two password changes.
            _store.RaiseFileVerified(new LargeArchiveFile { FileId = Guid.NewGuid() });
            _passwords.Raise(p => p.PasswordChanged += null, Guid.NewGuid(), 2);
            _passwords.Raise(p => p.PasswordChanged += null, Guid.NewGuid(), 3);

            releaseFirstRun.SetResult();

            // Idle after the second run with nothing pending: the three signals made exactly one more run, and no third
            // one comes before the period.
            var idle = await idleAfterTwoRuns.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await service.StopAsync(CancellationToken.None);

            Assert.Equal((2, false), idle);
            Assert.Equal(1, maxRunning);
        }

        [Fact]
        public async Task AFailingRun_IsCaught_AndTheCleanupRunsOnceADay()
        {
            _builder.Setup(b => b.RunDueAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broken"));
            _log.Setup(l => l.RemoveOlderThan(It.IsAny<DateTime>())).Throws(new InvalidOperationException("log broken"));

            using var service = CreateService();

            await service.RunOnceAsync(CancellationToken.None);
            await service.RunOnceAsync(CancellationToken.None);

            _log.Verify(l => l.RemoveOlderThan(new DateTime(2025, 11, 25, 12, 0, 0, DateTimeKind.Utc)), Times.Once);

            _time.Advance(TimeSpan.FromDays(1));
            await service.RunOnceAsync(CancellationToken.None);

            _log.Verify(l => l.RemoveOlderThan(It.IsAny<DateTime>()), Times.Exactly(2));
        }

        [Fact]
        public void AFinishedBuild_IsReportedToTheSensors()
        {
            using var service = CreateService();
            var fileId = Guid.NewGuid();

            _builder.Raise(b => b.BuildFinished += null, fileId, false, "no room for the archive");

            _monitor.Verify(m => m.ReportArchiveBuild(fileId, false, "no room for the archive"), Times.Once);
        }


        [Fact]
        public void TheSensors_HaveADefaultAlertOnError_AndPostTheBuildsAndTheLogChanges()
        {
            var sensors = new Dictionary<string, HistorySealingSensorsTests.Recording<bool>>();
            var options = new Dictionary<string, InstantSensorOptions>();
            var collector = new Mock<IDataCollector>(MockBehavior.Strict);
            collector.Setup(c => c.CreateBoolSensor(It.IsAny<string>(), It.IsAny<InstantSensorOptions>()))
                .Returns((string path, InstantSensorOptions sensorOptions) =>
                {
                    options[path] = sensorOptions;
                    return sensors[path] = new HistorySealingSensorsTests.Recording<bool>();
                });

            var monitor = new LargeFileSensors(collector.Object, null);

            var buildPath = $"{LargeFileSensors.NodeName}/{LargeFileSensors.ArchiveBuildResultName}";
            var logPath = $"{LargeFileSensors.NodeName}/{LargeFileSensors.DownloadLogName}";

            // Created at their first report: a server that never built an archive has neither.
            Assert.Empty(sensors);

            var fileId = Guid.NewGuid();
            monitor.ReportArchiveBuild(fileId, false, "packing failed: disk error");
            monitor.ReportArchiveBuild(fileId, true, null);

            Assert.Equal([(false, SensorStatus.Error), (true, SensorStatus.Ok)], sensors[buildPath].Values.Select(v => (v.Value, v.Status)));
            Assert.Contains("packing failed: disk error", sensors[buildPath].Values[0].Comment);

            monitor.ReportDownloadLog(true, null);
            monitor.ReportDownloadLog(true, null);
            monitor.ReportDownloadLog(false, "read-only");
            monitor.ReportDownloadLog(false, "read-only");
            monitor.ReportDownloadLog(true, null);

            Assert.Equal([(true, SensorStatus.Ok), (false, SensorStatus.Error), (false, SensorStatus.Error), (true, SensorStatus.Ok)],
                sensors[logPath].Values.Select(v => (v.Value, v.Status)));

            foreach (var path in new[] { buildPath, logPath })
            {
                var alert = Assert.Single(options[path].Alerts);
                var condition = Assert.Single(alert.Conditions);
                Assert.Equal(AlertProperty.Status, condition.Property);
                Assert.Equal(AlertOperation.IsChangedToError, condition.Operation);
                Assert.False(string.IsNullOrEmpty(alert.Template));
            }
        }


        [Theory]
        [InlineData(nameof(LargeFileArchivesConfig.DeflateLevel), 10)]
        [InlineData(nameof(LargeFileArchivesConfig.DeflateLevel), -1)]
        [InlineData(nameof(LargeFileArchivesConfig.PasswordRetentionDays), -1)]
        [InlineData(nameof(LargeFileArchivesConfig.DownloadLogWindowMinutes), -1)]
        [InlineData(nameof(LargeFileArchivesConfig.DownloadLogRetentionDays), 0)]
        [InlineData(nameof(LargeFileArchivesConfig.DownloadLogMaxCallers), 0)]
        public void TheConfiguration_RefusesValuesOutOfRange(string property, int value)
        {
            var config = new LargeFileArchivesConfig();
            typeof(LargeFileArchivesConfig).GetProperty(property).SetValue(config, value);

            var error = Assert.Throws<InvalidOperationException>(config.Validate);
            Assert.Contains($"LargeFileArchives.{property}", error.Message);
        }

        [Fact]
        public void TheConfiguration_Defaults()
        {
            var config = new LargeFileArchivesConfig();
            config.Validate();

            Assert.Equal((1, 30, 10, 365, 10_000),
                (config.DeflateLevel, config.PasswordRetentionDays, config.DownloadLogWindowMinutes, config.DownloadLogRetentionDays, config.DownloadLogMaxCallers));
        }


        [Theory]
        [InlineData("core", "dmp", "core.dmp.zip")]
        [InlineData("core.dmp", "dmp", "core.dmp.zip")]
        [InlineData("core", "", "core.zip")]
        [InlineData("../../etc/passwd", null, "passwd.zip")]
        [InlineData("C:\\dumps\\w3wp.dmp", null, "w3wp.dmp.zip")]
        [InlineData("C:w3wp", "dmp", "w3wp.dmp.zip")]
        [InlineData("..", null, "file.zip")]
        [InlineData("a\"b<c>|?*\u0001", null, "a_b_c____.zip")]
        public void TheDownloadName_IsTheOriginalNameWithoutAnyPath(string name, string extension, string expected) =>
            Assert.Equal(expected, LargeFileNames.DownloadFileName(name, extension));

        [Theory]
        [InlineData("report\u202Egpj.exe", "dmp", "reportgpj.exe.zip")]
        [InlineData("a\u200Bb\u2066c", "dmp", "abc.dmp.zip")]
        public void TheDownloadName_DropsFormatCharacters_AsTheEntryNameDoes(string name, string extension, string expected)
        {
            var fileName = LargeFileNames.DownloadFileName(name, extension);

            Assert.Equal(expected, fileName);
            Assert.Equal(ArchiveEntryNames.Sanitize(name, extension) + ".zip", fileName);

            // Ordinal: a culture-aware search ignores format characters and finds one anywhere.
            var header = LargeFileNames.ContentDisposition(fileName);
            Assert.Equal(-1, header.IndexOf('\u202E'));
            Assert.Equal(-1, header.IndexOf("%E2%80%AE", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void TheContentDisposition_CarriesAnAsciiFallbackAndTheFullName()
        {
            var header = LargeFileNames.ContentDisposition("Дамп 100%.dmp.zip");

            Assert.StartsWith("attachment", header);
            Assert.Contains("filename=\"____ 100_.dmp.zip\"", header);
            Assert.Contains("filename*=UTF-8''", header);
        }


        private sealed class ManualTime(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset _now = now;

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan by) => _now += by;
        }
    }
}
