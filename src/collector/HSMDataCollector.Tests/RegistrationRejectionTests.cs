using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1659 A2: what the command queue does with a registration the server answered.
    /// A 4xx is final: the same payload and key never succeed on repeat, so the package is
    /// reported once and not sent again (the native collector's rule since 0.10.3, #1515).
    /// A missing response or a 5xx is still sent again on the next cycle.
    /// </summary>
    public sealed class RegistrationRejectionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private const string RejectedPath = "rejection/first";
        private const string LaterPath = "rejection/later";
        private const string ValuePath = "rejection/value";

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.NotAcceptable)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task Registration_rejected_with_4xx_is_not_sent_again(HttpStatusCode status)
        {
            var sender = new ScriptedCommandSender(firstAnswer: status);

            using (var collector = CreateCollector(sender))
            {
                collector.CreateIntSensor(RejectedPath);
                await collector.Start();

                Assert.True(await sender.WaitForPackageWithAsync(RejectedPath, Timeout), "The registration was never sent.");

                // A second registration is the clock of this test: it can only be delivered by a
                // command package built after the rejected one came back, so by the time it lands
                // a re-enqueued registration has ridden with it (or ahead of it).
                collector.CreateIntSensor(LaterPath);

                Assert.True(await sender.WaitForPackageWithAsync(LaterPath, Timeout), "The later registration was never sent.");

                await collector.Stop();
            }

            Assert.Equal(1, sender.PackagesWith(RejectedPath));
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.BadGateway)]
        public async Task Registration_answered_5xx_is_sent_again(HttpStatusCode status)
        {
            var sender = new ScriptedCommandSender(firstAnswer: status);

            using (var collector = CreateCollector(sender))
            {
                collector.CreateIntSensor(RejectedPath);
                await collector.Start();

                Assert.True(await sender.WaitForPackagesWithAsync(RejectedPath, 2, Timeout),
                            $"A registration answered {(int)status} was not sent again; packages: {sender.PackagesWith(RejectedPath)}.");

                await collector.Stop();
            }
        }

        [Fact]
        public async Task Registration_without_a_response_is_sent_again()
        {
            var sender = new ScriptedCommandSender(firstAnswer: null);

            using (var collector = CreateCollector(sender))
            {
                collector.CreateIntSensor(RejectedPath);
                await collector.Start();

                Assert.True(await sender.WaitForPackagesWithAsync(RejectedPath, 2, Timeout),
                            $"A registration that got no response was not sent again; packages: {sender.PackagesWith(RejectedPath)}.");

                await collector.Stop();
            }
        }

        [Fact]
        public async Task Rejected_registration_is_reported_once_with_the_status()
        {
            var sender = new ScriptedCommandSender(firstAnswer: HttpStatusCode.BadRequest);
            var logger = new RecordingLogger();

            using (var collector = CreateCollector(sender))
            {
                collector.AddCustomLogger(logger);
                collector.CreateIntSensor(RejectedPath);
                await collector.Start();

                Assert.True(await sender.WaitForPackageWithAsync(RejectedPath, Timeout));

                collector.CreateIntSensor(LaterPath);
                Assert.True(await sender.WaitForPackageWithAsync(LaterPath, Timeout));

                await collector.Stop();
            }

            var reports = logger.Errors.Where(e => e.Contains("rejected")).ToList();

            Assert.True(reports.Count == 1, "Expected one rejection report, got: " + string.Join(" | ", logger.Errors));
            Assert.Contains("400", reports[0]);
            Assert.Contains("Queue: Command", reports[0]);
            // A lone registration is named, so the report says which sensor has no metadata.
            Assert.Contains(RejectedPath, reports[0]);
        }

        [Fact]
        public async Task Value_package_answered_4xx_is_still_sent_again()
        {
            // The scope of A2 is registrations. A value batch keeps the native collector's rule:
            // any failed send goes back to the queue, 4xx included.
            var sender = new ScriptedCommandSender(firstAnswer: HttpStatusCode.OK)
            {
                FirstDataAnswer = HttpStatusCode.BadRequest,
            };

            using (var collector = CreateCollector(sender))
            {
                var sensor = collector.CreateIntSensor(ValuePath);
                await collector.Start();

                sensor.AddValue(42);

                Assert.True(await sender.WaitForDataPackagesAsync(2, Timeout),
                            $"A value package answered 400 was not sent again; packages: {sender.DataPackages}.");

                await collector.Stop();
            }
        }

        [Fact]
        public void Sending_info_keeps_the_status_of_the_response()
        {
            using (var rejected = new HttpResponseMessage(HttpStatusCode.NotAcceptable))
            using (var accepted = new HttpResponseMessage(HttpStatusCode.OK))
            {
                Assert.Equal(406, new PackageSendingInfo(10, rejected).StatusCode);
                Assert.Equal(200, new PackageSendingInfo(10, accepted).StatusCode);
            }

            Assert.Null(new PackageSendingInfo(10, null, "no route to host").StatusCode);
            Assert.Null(default(PackageSendingInfo).StatusCode);
        }

        [Fact]
        public void Sending_info_built_from_a_bare_status_reads_like_one_built_from_a_response()
        {
            var accepted = new PackageSendingInfo(10, 200, null);
            var rejected = new PackageSendingInfo(10, 406, "the key is blocked");
            var failed = new PackageSendingInfo(10, 503, null);

            Assert.True(accepted.IsSuccess);
            Assert.Null(accepted.Error);

            Assert.False(rejected.IsSuccess);
            Assert.Equal(406, rejected.StatusCode);
            Assert.Equal("the key is blocked", rejected.Error);
            Assert.True(rejected.IsRejected);

            Assert.False(failed.IsSuccess);
            Assert.NotNull(failed.Error);
            Assert.False(failed.IsRejected);
        }


        private static DataCollector CreateCollector(IDataSender sender)
        {
            return new DataCollector(new CollectorOptions
            {
                AccessKey = "rejection-key",
                ClientName = "rejection-client",
                ComputerName = "rejection-host",
                Module = "rejection-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            });
        }


        /// <summary>
        /// Answers the first command package that carries <see cref="RejectedPath"/> (and optionally
        /// the first data package) with a scripted status, or with no response at all, and accepts
        /// everything after it.
        /// </summary>
        private sealed class ScriptedCommandSender : IDataSender
        {
            private readonly object _lock = new object();
            private readonly List<string[]> _commandPackages = new List<string[]>();
            private readonly HttpStatusCode? _firstAnswer;

            private int _commandAnswers;
            private int _dataPackages;

            internal ScriptedCommandSender(HttpStatusCode? firstAnswer)
            {
                _firstAnswer = firstAnswer;
            }

            internal HttpStatusCode FirstDataAnswer { get; set; } = HttpStatusCode.OK;

            internal int DataPackages => Volatile.Read(ref _dataPackages);

            internal int PackagesWith(string path)
            {
                lock (_lock)
                    return _commandPackages.Count(p => Carries(p, path));
            }

            internal Task<bool> WaitForPackageWithAsync(string path, TimeSpan timeout) => WaitForPackagesWithAsync(path, 1, timeout);

            internal Task<bool> WaitForPackagesWithAsync(string path, int count, TimeSpan timeout) =>
                WaitAsync(() => PackagesWith(path) >= count, timeout);

            internal Task<bool> WaitForDataPackagesAsync(int count, TimeSpan timeout) =>
                WaitAsync(() => DataPackages >= count, timeout);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
            {
                var paths = commands.Select(c => c.Path).ToArray();
                bool first;

                // Scripted is the first package that carries the registration under test, whatever
                // else the collector registers around it.
                lock (_lock)
                {
                    _commandPackages.Add(paths);
                    first = Carries(paths, RejectedPath) && _commandAnswers++ == 0;
                }

                if (!first)
                    return Answer(HttpStatusCode.OK);

                return _firstAnswer.HasValue
                    ? Answer(_firstAnswer.Value)
                    : new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, null, "No connection could be made"));
            }

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                // Self-diagnostic values share the queue; only the sensor under test counts.
                if (!items.Any(i => i.Path.EndsWith(ValuePath, StringComparison.Ordinal)))
                    return Answer(HttpStatusCode.OK);

                return Answer(Interlocked.Increment(ref _dataPackages) == 1 ? FirstDataAnswer : HttpStatusCode.OK);
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
                Answer(HttpStatusCode.OK);

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
                Answer(HttpStatusCode.OK);

            // The collector sends full paths: computer/module/sensor.
            private static bool Carries(string[] paths, string sensorPath) =>
                paths.Any(p => p.EndsWith(sensorPath, StringComparison.Ordinal));

            private static ValueTask<PackageSendingInfo> Answer(HttpStatusCode status)
            {
                using (var response = new HttpResponseMessage(status))
                    return new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, response));
            }

            private static async Task<bool> WaitAsync(Func<bool> done, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;

                while (!done())
                {
                    if (DateTime.UtcNow >= deadline)
                        return false;

                    await Task.Delay(10).ConfigureAwait(false);
                }

                return true;
            }
        }


        private sealed class RecordingLogger : ICollectorLogger
        {
            private readonly List<string> _errors = new List<string>();

            internal List<string> Errors
            {
                get
                {
                    lock (_errors)
                        return _errors.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message) { }

            public void Error(string message)
            {
                lock (_errors)
                    _errors.Add(message);
            }

            public void Error(Exception ex)
            {
                lock (_errors)
                    _errors.Add(ex.ToString());
            }
        }
    }
}
