#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// #1747: inside the stop drain the built-in HTTP client sends a value package once. A
    /// running collector repeats a failed value request inside the send (10 times, with a
    /// growing pause); in the drain those repeats spent the whole stop budget on a server that
    /// is down and delivered nothing.
    ///
    /// Each test lets the server fail exactly one request and accept the next. A send that is
    /// one attempt comes back failed after one request; a send that repeats comes back
    /// accepted after two. So nothing is timed, and nothing depends on how fast the machine is.
    ///
    /// Compiled into both collector test projects: the unit project runs it against the net472
    /// build, the integration project against net6.0.
    /// </summary>
    [Trait("Category", "FakeServerE2E")]
    public sealed class StopDrainSingleAttemptTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static CollectorOptions OptionsFor(FakeHsmServer server)
        {
            return new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "drain-attempt-key",
                ClientName = "drain-attempt-client",
                AllowPlaintextTransport = true,
                // The value stays in its queue until the stop: the drain is what sends it.
                PackageCollectPeriod = TimeSpan.FromHours(1),
            };
        }

        [Theory]
        [InlineData("data")]
        [InlineData("priority")]
        [InlineData("file")]
        public async Task Stop_drain_view_of_the_client_does_not_repeat_a_failed_value_request(string kind)
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                var drain = ((IStopDrainSender)client).StopDrain;

                server.FailNextDataRequests(1);

                var values = new SensorValueBase[] { new IntSensorValue { Path = "drain/attempt", Value = 1 } };
                var token = CancellationToken.None;

                var info = kind == "data" ? await drain.SendDataAsync(values, token)
                         : kind == "priority" ? await drain.SendPriorityDataAsync(values, token)
                         : await drain.SendFileAsync(new FileSensorValue { Path = "drain/attempt", Name = "drain", Extension = "txt" }, token);

                Assert.False(info.IsSuccess);
                Assert.Equal(503, info.StatusCode);
                Assert.Single(server.DataRequests);
            }
        }

        [Fact]
        public async Task Stop_against_a_server_that_fails_sends_the_value_package_once()
        {
            using (var server = new FakeHsmServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("drain/attempt");
                    await collector.Start();

                    Assert.True(await WaitForAsync(() => server.CommandRequests.Any(r => r.Answer == HttpStatusCode.OK)),
                                "The registration was never accepted.");

                    sensor.AddValue(4242);

                    server.FailNextDataRequests(1);

                    await collector.Stop();
                }

                var requests = server.DataRequests.ToList();

                Assert.Single(requests);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, requests[0].Answer);
                Assert.Contains("4242", requests[0].Body);
            }
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
    }
}
