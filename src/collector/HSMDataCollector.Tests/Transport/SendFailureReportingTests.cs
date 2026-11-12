#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
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

                    await collector.Stop();
                }
            }

            var reports = logger.Errors.Where(e => e.StartsWith("Failed to send data", StringComparison.Ordinal)).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report of the failing send, got {reports.Count}: " + string.Join(" | ", reports.Distinct()));
        }

        [Fact]
        public async Task Repeated_refusal_of_a_command_package_is_reported_once_per_window()
        {
            var logger = new RecordingLogger();

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

                    await collector.Stop();
                }
            }

            var reports = logger.Errors.Where(e => e.StartsWith("Command request has been faulted", StringComparison.Ordinal)).ToList();

            Assert.True(reports.Count == 1,
                        $"Expected one report of the refused commands, got {reports.Count}: " + string.Join(" | ", reports.Distinct()));
            Assert.Contains("503", reports[0]);
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
            Assert.Contains("503", reports[0]);
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
