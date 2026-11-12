#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// #1659 A1 and A4: what the collector says when a send fails.
    /// A1: the failure text carries the answered status and the server's own message, not the
    /// type name of the response content.
    /// A4: a failure that repeats is reported once per deduplication window, not once per attempt.
    ///
    /// Compiled into both collector test projects (net472 and net6.0 builds of the collector).
    /// </summary>
    [Trait("Category", "FakeServerE2E")]
    public sealed class SendFailureReportingTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static CollectorOptions OptionsFor(FakeHsmServer server)
        {
            return new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "failure-report-key",
                ClientName = "failure-report-client",
                AllowPlaintextTransport = true,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(100),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
            };
        }

        // --- A1 ---

        [Fact]
        public async Task Failure_text_carries_the_status_and_the_server_message()
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                server.AnswerNextDataRequest(HttpStatusCode.NotAcceptable, "Access key is blocked.");

                var info = await client.SendDataAsync(new[] { new IntSensorValue { Path = "report/int", Value = 1 } }, CancellationToken.None);

                Assert.False(info.IsSuccess);
                Assert.Equal(406, info.StatusCode);
                Assert.Contains("406", info.Error);
                Assert.Contains("Access key is blocked.", info.Error);
                Assert.DoesNotContain("System.Net.Http", info.Error);
            }
        }

        [Fact]
        public async Task Failure_text_does_not_repeat_a_body_that_is_not_plain_text()
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                // The /list route answers a 400 with the request batch itself, as JSON. Sensor
                // values do not belong in a log line or in the Collector errors sensor.
                server.AnswerNextDataRequest(HttpStatusCode.BadRequest, "[{\"Path\":\"report/int\",\"Value\":\"secret-value\"}]", "application/json");

                var info = await client.SendDataAsync(new[] { new IntSensorValue { Path = "report/int", Value = 1 } }, CancellationToken.None);

                Assert.False(info.IsSuccess);
                Assert.Contains("400", info.Error);
                Assert.DoesNotContain("secret-value", info.Error);
                Assert.DoesNotContain("System.Net.Http", info.Error);
            }
        }

        [Fact]
        public async Task Failure_text_keeps_a_long_server_message_short()
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                server.AnswerNextDataRequest(HttpStatusCode.NotAcceptable, new string('x', 5000));

                var info = await client.SendDataAsync(new[] { new IntSensorValue { Path = "report/int", Value = 1 } }, CancellationToken.None);

                Assert.Contains("406", info.Error);
                Assert.True(info.Error.Length < 600, $"The failure text is {info.Error.Length} characters long.");
            }
        }

        [Fact]
        public async Task Failure_text_of_a_server_error_is_the_same_whatever_its_body_says()
        {
            // The text is the key a failing package is deduplicated by. A proxy that answers a
            // 5xx with a page that names the request would make every cycle a new report.
            var texts = new List<string>();

            foreach (var body in new[] { "upstream failed, request id 1001", "upstream failed, request id 1002" })
                using (var response = new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(body, Encoding.UTF8, "text/plain") })
                    texts.Add((await BaseHandlers<SensorValueBase>.BuildSendingInfoAsync(10, response)).Error);

            Assert.Equal(texts[0], texts[1]);
            Assert.Contains("502", texts[0]);
            Assert.DoesNotContain("request id", texts[0]);
        }

        [Fact]
        public void Failure_text_built_from_a_response_alone_names_the_status_not_the_content_type()
        {
            // The public constructor a custom IDataSender uses. It cannot read the content
            // (that is asynchronous), so it reports the status and the reason phrase.
            using (var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("ignored") })
            {
                var info = new PackageSendingInfo(10, response);

                Assert.Contains("400", info.Error);
                Assert.Contains("Bad Request", info.Error);
                Assert.DoesNotContain("System.Net.Http", info.Error);
            }
        }

        // --- A4 ---

        [Fact]
        public async Task Repeated_failure_of_a_send_is_reported_once_per_window()
        {
            var logger = new RecordingLogger();
            List<string> running;

            using (var server = new FakeHsmServer())
            {
                // Every registration post is dropped without an answer, so every cycle of the
                // command queue is one failed send with the same cause.
                server.AbortNextCommandRequests(int.MaxValue);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    collector.CreateIntSensor("report/outage");
                    await collector.Start();

                    Assert.True(await WaitForAsync(() => server.CommandRequests.Count() >= 5), "The registration was not sent five times.");

                    // Read before the stop: its flush tries once more and says so in a line of
                    // its own ("queued for clear"), which is the stop's report, not a repeat.
                    running = logger.Errors;

                    await collector.Stop();
                }
            }

            // The queue reports the package it could not send; the send itself adds no line of
            // its own, so five failed cycles are one entry.
            var reports = running.Where(e => e.Contains("Failed to send")).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report of the failing send, got {reports.Count}: " + string.Join(" | ", reports.Distinct()));
            Assert.Contains("Queue: Command", reports[0]);
        }

        [Fact]
        public async Task Repeated_refusal_of_a_command_package_is_reported_once_per_window()
        {
            var logger = new RecordingLogger();
            List<string> running;

            using (var server = new FakeHsmServer())
            {
                // A server restarting behind its proxy: every registration post is answered 503.
                server.FailNextCommandRequests(int.MaxValue);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    collector.CreateIntSensor("report/restarting");
                    await collector.Start();

                    Assert.True(await WaitForAsync(() => server.CommandRequests.Count() >= 5), "The registration was not sent five times.");

                    running = logger.Errors;

                    await collector.Stop();
                }
            }

            // Every error line about the refused send counts, whatever its wording: the line
            // this replaced named the status by its enum name, not by its number.
            var reports = running.Where(e => e.Contains("503") || e.Contains("ServiceUnavailable") || e.Contains("Command") || e.Contains("Failed to send")).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report of the refused commands, got {reports.Count}: " + string.Join(" | ", reports.Distinct()));
            Assert.Contains("Queue: Command", reports[0]);
            Assert.Contains("503", reports[0]);
        }

        [Fact]
        public async Task Failing_package_that_grows_between_cycles_is_still_one_report()
        {
            var logger = new RecordingLogger();
            var sender = new RefusingDataSender();
            List<string> running;

            using (var collector = new DataCollector(new CollectorOptions
            {
                AccessKey = "report-key",
                ClientName = "report-client",
                ComputerName = "report-host",
                Module = "report-module",
                DataSender = sender,
                MaxQueueSize = 1000,
                MaxValuesInPackage = 50,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(1),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                MaxDeduplicatedMessages = 100,
            }))
            {
                collector.AddCustomLogger(logger);

                var sensor = collector.CreateIntSensor("report/growing");

                await collector.Start();

                // One more value before each of four failed sends: every package is larger than
                // the one before it, as it is while a key stays blocked or a server stays down.
                for (var cycle = 1; cycle <= 4; cycle++)
                {
                    sensor.AddValue(cycle);

                    Assert.True(await WaitForAsync(() => sender.LargestPackage >= cycle), $"No package of {cycle} value(s) was sent.");
                }

                running = logger.Errors;

                await collector.Stop();
            }

            var reports = running.Where(e => e.Contains("Failed to send")).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report of the failing send, got {reports.Count}: " + string.Join(" | ", reports.Distinct()));
        }

        [Fact]
        public void Cause_of_a_send_without_an_answer_keeps_the_innermost_message()
        {
            var refused = new System.Net.Http.HttpRequestException("An error occurred while sending the request.",
                                                                   new System.IO.IOException("Unable to connect.", new InvalidOperationException("No connection could be made because the target machine actively refused it")));

            var text = PackageSendingInfo.DescribeException(refused);

            Assert.Contains("An error occurred while sending the request.", text);
            Assert.Contains("actively refused", text);
            Assert.Equal("plain", PackageSendingInfo.DescribeException(new InvalidOperationException("plain")));
        }

        [Fact]
        public async Task Sender_that_came_with_the_options_keeps_its_own_error_reports()
        {
            var clientLogger = new RecordingLogger();
            var collectorLogger = new RecordingLogger();

            using (var server = new FakeHsmServer())
            {
                server.FailNextDataRequests(1); // one retry inside the send

                var options = OptionsFor(server);
                var clientLog = new LoggerManager();
                clientLog.AddLogger(clientLogger);

                // The host's own client: the collector must not redirect what it reports.
                options.DataSender = new HsmHttpsClient(options, clientLog);

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(collectorLogger);
                    var sensor = collector.CreateIntSensor("report/foreign");
                    await collector.Start();

                    sensor.AddValue(7);

                    Assert.True(await WaitForAsync(() => server.DataRequests.Count(r => r.Body.Contains("report/foreign")) >= 2),
                                "The value was not retried.");

                    await collector.Stop();
                }
            }

            Assert.Contains(clientLogger.Errors, e => e.StartsWith("Failed to send data", StringComparison.Ordinal));
            Assert.DoesNotContain(collectorLogger.Errors, e => e.StartsWith("Failed to send data", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Retries_inside_one_send_are_reported_once()
        {
            var logger = new RecordingLogger();

            using (var server = new FakeHsmServer())
            {
                server.FailNextDataRequests(2); // 503, 503, then 200: two retries inside the send

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("report/retried");
                    await collector.Start();

                    sensor.AddValue(7);

                    Assert.True(await WaitForAsync(() => server.DataRequests.Count(r => r.Body.Contains("report/retried")) >= 3),
                                "The value was not retried past the two failures.");

                    await collector.Stop();
                }
            }

            var reports = logger.Errors.Where(e => e.StartsWith("Failed to send data", StringComparison.Ordinal)).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report for the two failed attempts, got {reports.Count}: " + string.Join(" | ", reports));
            // The cause is spelled as in the queue's report of a send that failed for good.
            Assert.Contains("Code: 503 (Service Unavailable).", reports[0]);
        }

        private static async Task<bool> WaitForAsync(Func<bool> done)
        {
            var deadline = DateTime.UtcNow + Timeout;

            while (!done())
            {
                if (DateTime.UtcNow >= deadline)
                    return false;

                await Task.Delay(20).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Accepts registrations and fails every value package without an answer, remembering
        /// the size of the largest package of the sensor under test it was handed.
        /// </summary>
        private sealed class RefusingDataSender : IDataSender
        {
            private int _largestPackage;

            internal int LargestPackage => Volatile.Read(ref _largestPackage);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<HSMSensorDataObjects.CommandRequestBase> commands, CancellationToken token) => Ok();

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                var count = items.Count(i => i.Path.EndsWith("report/growing", StringComparison.Ordinal));

                if (count == 0)
                    return Ok();

                if (count > Volatile.Read(ref _largestPackage))
                    Volatile.Write(ref _largestPackage, count);

                return new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, null, "No connection could be made"));
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Ok();

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Ok();

            private static ValueTask<PackageSendingInfo> Ok()
            {
                using (var response = new HttpResponseMessage(HttpStatusCode.OK))
                    return new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, response));
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
