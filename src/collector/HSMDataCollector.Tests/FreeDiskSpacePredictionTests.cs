using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    public sealed class FreeDiskSpacePredictionTests
    {
        [Fact]
        public void FreeDiskSpace_reports_disk_read_failure_as_error_status()
        {
            using (var collector = CreateCollector())
            {
                var sensor = new TestFreeDiskSpace(
                    CreateOptions(collector, new ThrowingDiskInfo(new IOException("statvfs failed"))));
                Exception observedException = null;

                sensor.ExceptionThrowing += (_, ex) => observedException = ex;

                var value = sensor.ReadValue();

                Assert.Equal(default, value);
                Assert.Equal(SensorStatus.Error, sensor.ReadStatus());
                Assert.Equal("statvfs failed", sensor.ReadComment());
                Assert.IsType<IOException>(observedException);
            }
        }

        [Fact]
        public async Task StartAsync_reports_initial_disk_read_failure_without_throwing()
        {
            using (var collector = CreateCollector())
            {
                var sensor = new TestFreeDiskSpacePrediction(
                    CreateOptions(collector),
                    new ThrowingDiskInfo(new IOException("statvfs failed")));
                Exception observedException = null;

                sensor.ExceptionThrowing += (_, ex) => observedException = ex;

                var exception = await Record.ExceptionAsync(async () => await sensor.StartAsync().AsTask()).ConfigureAwait(false);

                await sensor.StopAsync().ConfigureAwait(false);

                Assert.Null(exception);
                Assert.IsType<IOException>(observedException);
            }
        }

        [Fact]
        public async Task Disk_speed_sampler_reports_disk_read_failure_without_throwing()
        {
            using (var collector = CreateCollector())
            {
                var diskInfo = new ThrowingDiskInfo(new IOException("statvfs failed"));
                var sensor = new TestFreeDiskSpacePrediction(CreateOptions(collector), diskInfo);
                var exceptions = new List<Exception>();

                sensor.ExceptionThrowing += (_, ex) => exceptions.Add(ex);

                await sensor.StartAsync().ConfigureAwait(false);

                var sampler = typeof(FreeDiskSpacePredictionBase)
                    .GetMethod("UpdateDiskSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

                var exception = Record.Exception(() => sampler.Invoke(sensor, null));

                await sensor.StopAsync().ConfigureAwait(false);

                Assert.Null(exception);
                Assert.All(exceptions, ex => Assert.IsType<IOException>(ex));
                Assert.True(exceptions.Count >= 2);
            }
        }

        // ---- Prediction formula (#1445) --------------------------------------------------------
        // One scripted free-space series per state, driven through an injected clock so the folding
        // math is asserted against exact intervals - and so a 10-minute sampling period costs the
        // suite nothing. The native unit tests (NativeDiskPrediction* in hsm_collector_tests.cpp)
        // run the SAME series and assert the SAME numbers, so the two collectors are pinned to one
        // set of values rather than to each other's opinion (repo rule #10).

        [Fact]
        public async Task Prediction_reports_a_real_estimate_while_the_disk_drains_steadily()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 3))
            {
                // Three 10-minute intervals, 300 MiB consumed each: a steady 0.5 MiB/sec.
                probe.DrainSteadily(3);

                var post = probe.Post();

                // 49 100 MiB left at 0.5 MiB/sec = 98 200 s (a day and three hours).
                Assert.Equal(TimeSpan.FromSeconds(98200), post.Value);
                Assert.Equal(SensorStatus.Ok, post.Status);
                Assert.Equal("Free space decreases by 0.5 Mbytes/sec.", post.Comment);
            }
        }

        [Fact]
        public async Task Prediction_decays_when_the_drain_stops()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 3))
            {
                probe.DrainSteadily(3);

                var draining = probe.Post();

                // The burst is over: ten flat intervals (100 minutes) with not a byte written.
                probe.Idle(10);

                var relaxed = probe.Post();

                // The estimate must MOVE AWAY from doom as the burst ages out of the EMA. Before
                // #1445 the EMA folded positive samples only, so `relaxed` was bit-for-bit `draining`
                // and the sensor predicted a full disk forever on an idle host.
                Assert.True(
                    relaxed.Value > draining.Value,
                    $"the estimate must decay while the disk is idle, got {relaxed.Value} after {draining.Value}");

                // 0.5 MiB/sec * (36/37)^10 = 0.38017 MiB/sec over 49 100 MiB. The rate halves every
                // 4.22 h, so 100 minutes of idling is worth about a third of the estimate - the
                // deliberate price of a six-hour window.
                Assert.Equal(129152769L, (long)relaxed.Value.TotalMilliseconds);
                Assert.Equal(SensorStatus.Ok, relaxed.Status);

                // The mantissa is left out of this assertion on purpose: net472 renders a double with
                // 15 significant digits and net6.0 with the shortest round-trip form, a pre-existing
                // divergence that the number-format conformance matrix already excludes.
                Assert.StartsWith("Free space decreases by 0.380169937438", relaxed.Comment);
                Assert.EndsWith(" Mbytes/sec.", relaxed.Comment);
            }
        }

        [Fact]
        public async Task Prediction_reports_growth_when_space_is_freed()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 1))
            {
                // 300 MiB freed over one 10-minute interval seeds the EMA at -0.5 MiB/sec.
                // Unreachable before #1445, because a negative sample was discarded, not folded in.
                probe.Sample(SeedFreeSpace + DrainPerInterval);

                var post = probe.Post();

                Assert.Equal(FreeDiskSpacePredictionBase.MaxPrediction, post.Value);
                Assert.Equal(SensorStatus.OffTime, post.Status);
                Assert.Equal(
                    "Free space increases by 0.5 Mbytes/sec. Value cannot be calculated.",
                    post.Comment);
            }
        }

        [Fact]
        public async Task Prediction_relaxes_instead_of_pinning_doom_when_free_space_flaps()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 1))
            {
                // One 300 MiB write, then ten intervals alternating 300 MiB freed / 300 MiB written.
                probe.Sample(SeedFreeSpace - DrainPerInterval);

                var first = probe.Post();

                for (var i = 0; i < 10; i++)
                    probe.Sample(i % 2 == 0 ? SeedFreeSpace : SeedFreeSpace - DrainPerInterval);

                var last = probe.Post();

                Assert.Equal(99400000L, (long)first.Value.TotalMilliseconds);
                Assert.Equal(130168963L, (long)last.Value.TotalMilliseconds);
                Assert.Equal(SensorStatus.Ok, last.Status);

                // A disk that writes as much as it frees must not converge on an alarming estimate:
                // the anti-correlated samples cancel and the horizon moves further out.
                Assert.True(
                    last.Value > first.Value,
                    $"flapping must relax the estimate, got {last.Value} after {first.Value}");
            }
        }

        [Fact]
        public async Task Prediction_reports_no_drain_on_a_completely_idle_disk()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 3))
            {
                probe.Idle(5);

                var post = probe.Post();

                // An explicit state, not a stale number and not TimeSpan.Zero: "nothing is draining"
                // has to be distinguishable from a real estimate on the server.
                Assert.Equal(FreeDiskSpacePredictionBase.MaxPrediction, post.Value);
                Assert.Equal(SensorStatus.OffTime, post.Status);
                Assert.Equal("Free space is not decreasing. Value cannot be calculated.", post.Comment);
            }
        }

        [Fact]
        public async Task Prediction_clamps_an_absurdly_slow_drain_to_the_ceiling()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 1, freeSpace: 2_000_000_000_000_000_000L))
            {
                // 1 KiB/sec over two exabytes of free space: ~62 million years, which
                // TimeSpan.FromSeconds cannot represent at all — it threw before #1445, and the
                // sensor then posted nothing at all while still looking healthy.
                probe.Sample(2_000_000_000_000_000_000L - 614_400L);

                var post = probe.Post();

                Assert.Equal(FreeDiskSpacePredictionBase.MaxPrediction, post.Value);
                Assert.Equal(SensorStatus.OffTime, post.Status);
                Assert.Equal(
                    "Free space decreases by 0.0009765625 Mbytes/sec. More than 365 days left.",
                    post.Comment);
            }
        }

        [Fact]
        public async Task Prediction_matches_the_hour_scale_drain_of_the_reported_host()
        {
            using (var probe = await PredictionProbe.CreateAsync(
                calibrationRequests: 3, freeSpace: GarageFreeSpace))
            {
                // The series from the #1445 report: 51 GB free, losing ~1.3 GB/h. The true answer is
                // "about a day and a half", and the sensor must say so as soon as calibration ends -
                // three measurements, i.e. 30 minutes after Start.
                probe.Drain(3, GarageDrainPerInterval);

                var firstEstimate = probe.Post();

                Assert.Equal(SensorStatus.Ok, firstEstimate.Status);
                Assert.Equal(139430770L, (long)firstEstimate.Value.TotalMilliseconds);
                Assert.Equal(1.61, Math.Round(firstEstimate.Value.TotalDays, 2));

                // Four more hours of the same drain: the rate is unchanged, so the estimate is just
                // the countdown of the free space that is actually gone. The old minute-scale window
                // swung by 4x between adjacent posts on this host.
                probe.Drain(21, GarageDrainPerInterval);

                var later = probe.Post();

                Assert.Equal(126830770L, (long)later.Value.TotalMilliseconds);
                Assert.Equal(1.47, Math.Round(later.Value.TotalDays, 2));

                // The implied rate - the free space remaining over the reported horizon - is the real
                // 361 111.11 bytes/sec at both ends of those four hours, to within a thousandth.
                Assert.Equal(361111.11, ImpliedRate(firstEstimate, 3), 2);
                Assert.Equal(361111.11, ImpliedRate(later, 24), 2);
            }
        }

        /// <summary>The drain rate the posted horizon implies, after <paramref name="intervals"/> drains.</summary>
        private static double ImpliedRate(Post post, int intervals) =>
            (GarageFreeSpace - intervals * GarageDrainPerInterval) / post.Value.TotalSeconds;

        [Fact]
        public async Task A_ten_minute_write_burst_barely_moves_the_estimate()
        {
            using (var probe = await PredictionProbe.CreateAsync(
                calibrationRequests: 3, freeSpace: GarageFreeSpace))
            {
                probe.Drain(24, GarageDrainPerInterval);

                var steady = probe.Post();

                // One 10-minute interval at FOUR TIMES the drain - the size of swing the reported
                // host showed between adjacent posts. A single sample can move the estimate by at
                // most the smoothing factor (2.7 %) of the distance to what it just measured, so
                // this costs under a tenth of the horizon instead of most of it.
                probe.Drain(1, GarageDrainPerInterval * 4);

                var afterBurst = probe.Post();

                Assert.Equal(115098462L, (long)afterBurst.Value.TotalMilliseconds);
                Assert.Equal(SensorStatus.Ok, afterBurst.Status);

                var swing = 1.0 - afterBurst.Value.TotalSeconds / steady.Value.TotalSeconds;

                Assert.True(
                    swing < 0.10,
                    $"a 10-minute burst must not move the estimate by a tenth, moved {swing:P2}");
            }
        }

        [Fact]
        public async Task A_failed_initial_disk_read_does_not_poison_the_drain_speed()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 1, failFirstRead: true))
            {
                // The Start read threw, so there is no baseline: the next successful read establishes
                // one and only the interval AFTER it is a measurement. Folding a signed sample against
                // a phantom zero baseline would instead seed the EMA at roughly minus the whole disk
                // per interval and park the sensor in the growing state for the best part of an hour.
                probe.DrainSteadily(2);

                var post = probe.Post();

                Assert.Equal(TimeSpan.FromSeconds(98800), post.Value);
                Assert.Equal(SensorStatus.Ok, post.Status);
                Assert.Equal("Free space decreases by 0.5 Mbytes/sec.", post.Comment);
            }
        }

        [Fact]
        public async Task Calibration_counts_free_space_measurements_not_posts()
        {
            using (var probe = await PredictionProbe.CreateAsync(calibrationRequests: 3))
            {
                probe.DrainSteadily(2);

                // Three posts in a row without a single new measurement in between: the counter must
                // not move. Before #1445 it advanced per POST, so a 5 min post cadence finished a
                // "3 measurement" calibration without ever having three measurements.
                for (var i = 0; i < 3; i++)
                {
                    var calibrating = probe.Post();

                    Assert.Equal("Calibration request (2/3). Value cannot be calculated yet.", calibrating.Comment);
                    Assert.Equal(SensorStatus.OffTime, calibrating.Status);

                    // And never TimeSpan.Zero, which an alert reads as "the disk is full NOW".
                    Assert.Equal(FreeDiskSpacePredictionBase.MaxPrediction, calibrating.Value);
                    Assert.NotEqual(TimeSpan.Zero, calibrating.Value);
                }

                probe.DrainSteadily(1);

                var predicting = probe.Post();

                Assert.Equal(SensorStatus.Ok, predicting.Status);
                Assert.StartsWith("Free space decreases by ", predicting.Comment);
            }
        }


        // MiB-aligned on purpose, and a power-of-two rate: the drain rate the comment renders is
        // then an exact short decimal that net472's 15-digit formatter and net6.0's
        // shortest-round-trip formatter agree on, and it is a fixed point of the EMA, so a steady
        // drain is reported exactly rather than to within an ulp.
        private const long SeedFreeSpace = 52_428_800_000L;     // 50 000 MiB
        private const long DrainPerInterval = 314_572_800L;     // 300 MiB per 10 min interval = 0.5 MiB/sec

        // The host from the #1445 report, in its own numbers: 51 GB free, draining ~1.3 GB/h. Used
        // by the two tests that check the retuned window against the real series rather than against
        // a convenient one, so these are decimal GB and the rate is not a round MiB figure.
        private const long GarageFreeSpace = 51_000_000_000L;
        private const long GarageDrainPerInterval = 216_666_666L; // per 10 min = 1.3 GB/h

        private sealed class PredictionProbe : IDisposable
        {
            private static readonly TimeSpan _interval =
                TimeSpan.FromSeconds(FreeDiskSpacePredictionBase.DefaultSpaceCheckPeriodInSec);

            private readonly DataCollector _collector;
            private readonly MutableDiskInfo _disk;
            private readonly TestFreeDiskSpacePrediction _sensor;

            private DateTime _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // The scripted free space, tracked here rather than read back off the disk so a probe can
            // also script a READ FAILURE without the series losing its place.
            private long _cursor;

            private PredictionProbe(
                DataCollector collector, MutableDiskInfo disk, TestFreeDiskSpacePrediction sensor, long freeSpace)
            {
                _collector = collector;
                _disk = disk;
                _sensor = sensor;
                _cursor = freeSpace;
            }

            public static async Task<PredictionProbe> CreateAsync(
                int calibrationRequests, long freeSpace = SeedFreeSpace, bool failFirstRead = false)
            {
                var collector = CreateCollector();
                var disk = new MutableDiskInfo { FreeSpace = freeSpace, FailNextRead = failFirstRead };
                var options = CreateOptions(collector);

                options.CalibrationRequests = calibrationRequests;

                // Long enough that neither real schedule fires: the probe drives both loops by hand.
                options.PostDataPeriod = TimeSpan.FromHours(1);
                options.SpaceCheckPeriod = TimeSpan.FromHours(1);

                var sensor = new TestFreeDiskSpacePrediction(options, disk);
                var probe = new PredictionProbe(collector, disk, sensor, freeSpace);

                sensor.UtcNowProvider = () => probe._now;

                await sensor.StartAsync().ConfigureAwait(false);

                return probe;
            }

            /// <summary>One sampling interval that ends with <paramref name="freeSpace"/> bytes free.</summary>
            public void Sample(long freeSpace)
            {
                _now += _interval;
                _cursor = freeSpace;
                _disk.FreeSpace = freeSpace;
                _sensor.UpdateDiskSpeed();
            }

            public void DrainSteadily(int intervals)
            {
                Drain(intervals, DrainPerInterval);
            }

            public void Drain(int intervals, long perInterval)
            {
                for (var i = 0; i < intervals; i++)
                    Sample(_cursor - perInterval);
            }

            public void Idle(int intervals)
            {
                for (var i = 0; i < intervals; i++)
                    Sample(_cursor);
            }

            /// <summary>One post, read in the order MonitoringSensorBase builds a sensor value.</summary>
            public Post Post()
            {
                var value = _sensor.ReadValue();

                return new Post(value, _sensor.ReadStatus(), _sensor.ReadComment());
            }

            public void Dispose()
            {
                _sensor.StopAsync().AsTask().GetAwaiter().GetResult();
                _collector.Dispose();
            }
        }

        private sealed class Post
        {
            public Post(TimeSpan value, SensorStatus status, string comment)
            {
                Value = value;
                Status = status;
                Comment = comment;
            }

            public TimeSpan Value { get; }

            public SensorStatus Status { get; }

            public string Comment { get; }
        }

        private sealed class MutableDiskInfo : IDiskInfo
        {
            private long _freeSpace;

            /// <summary>Makes the next read throw once, like a drive that is briefly unavailable.</summary>
            public bool FailNextRead { get; set; }

            public long FreeSpace
            {
                get
                {
                    if (FailNextRead)
                    {
                        FailNextRead = false;

                        throw new IOException("drive is not ready");
                    }

                    return _freeSpace;
                }
                set => _freeSpace = value;
            }

            public long FreeSpaceMb => _freeSpace / (1024L * 1024L);

            public string DiskLetter => "C";
        }

        private static DataCollector CreateCollector()
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "disk-prediction-key",
                ClientName = "disk-prediction-client",
                ComputerName = "disk-prediction-host",
                Module = "disk-prediction-module",
                DataSender = new NoopSender(),
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMilliseconds(100),
                MaxDeduplicatedMessages = 100,
            });
        }

        private static DiskSensorOptions CreateOptions(DataCollector collector)
        {
            return new DiskSensorOptions
            {
                Path = "disk/prediction",
                DataProcessor = GetDataProcessor(collector),
                PostDataPeriod = TimeSpan.FromSeconds(1),
                CalibrationRequests = 1,
            };
        }

        private static DiskSensorOptions CreateOptions(DataCollector collector, IDiskInfo diskInfo) =>
            CreateOptions(collector).SetInfo(diskInfo);

        private static DataProcessor GetDataProcessor(DataCollector collector) =>
            (DataProcessor)typeof(DataCollector)
                .GetField("_dataProcessor", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(collector);

        private sealed class TestFreeDiskSpacePrediction : FreeDiskSpacePredictionBase
        {
            internal TestFreeDiskSpacePrediction(DiskSensorOptions options, IDiskInfo diskInfo)
                : base(options, diskInfo) { }

            public TimeSpan ReadValue() => GetValue();

            public string ReadComment() => GetComment();

            public SensorStatus ReadStatus() => GetStatus();
        }

        private sealed class TestFreeDiskSpace : FreeDiskSpaceBase
        {
            internal TestFreeDiskSpace(DiskSensorOptions options) : base(options) { }

            public double ReadValue() => GetValue();

            public string ReadComment() => GetComment();

            public SensorStatus ReadStatus() => GetStatus();
        }

        private sealed class ThrowingDiskInfo : IDiskInfo
        {
            private readonly Exception _exception;

            public ThrowingDiskInfo(Exception exception) => _exception = exception;

            public long FreeSpaceMb => throw _exception;

            public long FreeSpace => throw _exception;

            public string DiskLetter => "/";
        }

        private sealed class NoopSender : IDataSender
        {
            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => default;

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => default;
        }
    }
}
