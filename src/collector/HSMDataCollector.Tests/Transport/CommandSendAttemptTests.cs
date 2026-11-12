#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorRequests;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// #1659 A3: one command send is one attempt. The command pipeline used to retry a failed
    /// connection inside the send, without a limit and with a growing pause (up to 2 minutes),
    /// so one unreachable-server send held the command queue for as long as the outage lasted.
    /// Now a send that gets no answer returns, and the queue sends the package again on its next
    /// cycle, which is what the native collector does.
    ///
    /// Compiled into both collector test projects: the unit project runs it against the net472
    /// build, the integration project against net6.0.
    /// </summary>
    [Trait("Category", "FakeServerE2E")]
    public sealed class CommandSendAttemptTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static CollectorOptions OptionsFor(FakeHsmServer server)
        {
            return new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "command-attempt-key",
                ClientName = "command-attempt-client",
                AllowPlaintextTransport = true,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(200),
            };
        }

        [Fact]
        public async Task Command_send_that_gets_no_answer_returns_after_one_attempt()
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                // More dropped connections than one send may use: a send that retries inside
                // itself would walk through them and come back with the accepted answer.
                server.AbortNextCommandRequests(2);

                var send = client.SendCommandAsync(new[] { new AddOrUpdateSensorRequest { Path = "attempt/single" } }, CancellationToken.None).AsTask();

                Assert.True(ReferenceEquals(await Task.WhenAny(send, Task.Delay(Timeout)), send), "The command send did not return.");

                var info = await send;

                Assert.False(info.IsSuccess);
                Assert.NotNull(info.Error);
                Assert.Null(info.StatusCode);
                Assert.Equal(1, server.CommandRequests.Count(r => r.Body.Contains("attempt/single")));
            }
        }

        [Fact]
        public async Task Failed_command_send_does_not_hold_later_commands_behind_its_retries()
        {
            using (var server = new FakeHsmServer())
            {
                // The server is "down" until the test brings it back: however many attempts fit
                // in between, none is answered, so nothing here depends on how fast they come.
                server.AbortNextCommandRequests(int.MaxValue);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.CreateIntSensor("attempt/first");
                    await collector.Start();

                    Assert.True(await WaitForAsync(() => server.CommandRequests.Any(r => r.Body.Contains("attempt/first"))),
                                "The first registration was never sent.");

                    // Registered while the first send is failing. The queue takes the failed package
                    // back and builds the next one from everything it holds, so the two travel
                    // together. A send that keeps retrying by itself delivers the first registration
                    // alone and leaves this one waiting behind it.
                    collector.CreateIntSensor("attempt/second");

                    server.AbortNextCommandRequests(0);

                    Assert.True(await WaitForAsync(() => server.CommandRequests.Any(r => r.Answer != 0 && r.Body.Contains("attempt/second"))),
                                "The second registration was never accepted.");

                    await collector.Stop();
                }

                var accepted = server.CommandRequests.Where(r => r.Answer != 0 && r.Body.Contains("attempt/second")).ToList();

                Assert.Single(accepted);
                Assert.Contains("attempt/first", accepted[0].Body);
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
