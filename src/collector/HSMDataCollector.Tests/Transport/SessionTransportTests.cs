#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Client.Session;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// Epic #1660 step 3: the collector session as the transport of the managed collector
    /// (<c>CollectorOptions.Transport = Session</c>). Every test runs a real collector against
    /// <see cref="ScriptedSessionServer"/> over a real WebSocket.
    ///
    /// Compiled into both collector test projects (net472 and net6.0 builds of the collector).
    /// </summary>
    [Trait("Category", "FakeServerE2E")]
    public sealed class SessionTransportTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static CollectorOptions OptionsFor(ScriptedSessionServer server)
        {
            return new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "session-key",
                ClientName = "session-client",
                ComputerName = "session-host",
                Module = "session-module",
                AllowPlaintextTransport = true,
                Transport = CollectorTransport.Session,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(10),
                ExceptionDeduplicatorWindow = TimeSpan.FromMinutes(10),
                SessionTimings = new SessionTimings
                {
                    MinBackoff = TimeSpan.FromMilliseconds(20),
                    MaxBackoff = TimeSpan.FromMilliseconds(100),
                },
            };
        }

        // --- the transport and nothing beside it ---

        [Fact]
        public async Task Values_and_registrations_travel_on_the_session_and_nothing_over_http()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/int");
                    await collector.Start();

                    sensor.AddValue(31337);

                    Assert.True(await WaitForAsync(() => server.AcceptedElements("list").Any(e => e.Contains("31337"))), "The value never arrived.");
                    Assert.True(await WaitForAsync(() => server.AcceptedElements("commands").Any(e => e.Contains("session/int"))), "The registration never arrived.");

                    await collector.Stop();
                }

                Assert.Empty(server.HttpRequests);
                Assert.Equal(1, server.Sessions);

                var headers = server.UpgradeHeaders;

                Assert.Equal("session-key", headers["Key"]);
                Assert.Equal("session-client", headers["ClientName"]);
                Assert.Equal("managed", headers["X-Collector-Kind"]);
                Assert.Matches(@"^\d+\.\d+\.\d+$", headers["X-Collector-Version"]);
                Assert.Contains(ScriptedSessionServer.Subprotocol, headers["Sec-WebSocket-Protocol"]);
            }
        }

        [Fact]
        public async Task Frame_body_is_the_http_body_byte_for_byte()
        {
            var values = new SensorValueBase[]
            {
                new IntSensorValue { Path = "wire/int", Value = 42, Comment = "quote\" and é", Time = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc) },
                new DoubleSensorValue { Path = "wire/double", Value = 0.1, Time = new DateTime(2026, 11, 10, 10, 0, 1, DateTimeKind.Utc) },
                new StringSensorValue { Path = "wire/string", Value = "<tag> & 'text'", Time = new DateTime(2026, 11, 10, 10, 0, 2, DateTimeKind.Utc) },
                new BoolSensorValue { Path = "wire/bool", Value = true, Time = new DateTime(2026, 11, 10, 10, 0, 3, DateTimeKind.Utc) },
            };
            var commands = new CommandRequestBase[]
            {
                new AddOrUpdateSensorRequest { Path = "wire/int", SensorType = SensorType.IntSensor, Description = "first" },
                new AddOrUpdateSensorRequest { Path = "wire/double", SensorType = SensorType.DoubleSensor },
            };

            using (var server = new ScriptedSessionServer())
            using (var sender = new SessionDataSender(OptionsFor(server), new LoggerManager()))
            {
                sender.Start();

                Assert.True(await WaitForAsync(() => sender.Connection.IsUp), "The session never came up.");

                Assert.True((await sender.SendDataAsync(values, CancellationToken.None)).IsSuccess);
                Assert.True((await sender.SendCommandAsync(commands, CancellationToken.None)).IsSuccess);

                await sender.StopAsync(terminal: false);

                var list = server.Requests.Single(r => r.Op == "list");
                var registration = server.Requests.Single(r => r.Op == "commands");

                // What the HTTP client would have posted to /list and /commands for the same input.
                Assert.Equal(new HttpRequest<SensorValueBase>(values, "unused").Content, list.Body);
                Assert.Equal(new HttpRequest<CommandRequestBase>(commands, "unused").Content, registration.Body);

                Assert.Equal($"{{\"id\":{list.Id},\"op\":\"list\",\"body\":{list.Body}}}", list.Frame);
            }
        }

        // --- no session, no fallback ---

        [Fact]
        public async Task Values_wait_while_the_upgrade_is_refused_and_arrive_when_it_is_accepted()
        {
            using (var server = new ScriptedSessionServer())
            {
                server.RefuseUpgrades(503);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/queued");
                    await collector.Start();

                    for (var i = 0; i < 5; i++)
                        sensor.AddValue(100 + i);

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 3), "The collector did not keep trying to open a session.");

                    // Refused three times and counting: nothing was delivered, and nothing went
                    // looking for another way in.
                    Assert.Empty(server.Requests);
                    Assert.Empty(server.HttpRequests);

                    server.AcceptUpgrades();

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Count >= 5), "The queued values never arrived.");

                    await collector.Stop();
                }

                Assert.Equal(new[] { 100, 101, 102, 103, 104 }, DeliveredInts(server).OrderBy(v => v).ToArray());
                Assert.Empty(server.HttpRequests);
            }
        }

        [Fact]
        public async Task Refused_upgrade_is_reported_once_and_the_waiting_queue_stays_quiet()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                server.RefuseUpgrades(404);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/refused");
                    await collector.Start();

                    sensor.AddValue(1);

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 5), "The collector did not keep trying.");

                    // Several collect cycles went by with a value that could not be sent.
                    await Task.Delay(300);

                    await collector.Stop();
                }
            }

            var reports = logger.Errors.Where(e => e.Contains("Cannot open a collector session")).ToList();

            Assert.True(reports.Count == 1, "Expected one report of the refused upgrade, got: " + string.Join(" | ", logger.Errors));
            Assert.DoesNotContain(logger.Errors, e => e.Contains("Failed to send package"));
        }

        // --- the frame limit ---

        [Fact]
        public async Task Batch_is_split_so_that_every_frame_fits_the_limit()
        {
            using (var server = new ScriptedSessionServer { MaxFrameBytes = 1100 })
            {
                var options = OptionsFor(server);
                options.PackageCollectPeriod = TimeSpan.FromHours(1); // the stop drain sends one package

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateIntSensor("session/split");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/split"), "The session never came up.");

                    for (var i = 0; i < 40; i++)
                        sensor.AddValue(i);

                    await collector.Stop();
                }

                var frames = server.Requests.Where(r => r.Op == "list").ToList();

                Assert.True(frames.Count > 1, "40 values fitted one 1100-byte frame.");
                Assert.All(frames, f => Assert.True(System.Text.Encoding.UTF8.GetByteCount(f.Frame) <= 1100, $"A frame of {f.Frame.Length} bytes went out."));
                Assert.Equal(Enumerable.Range(0, 40).ToArray(), DeliveredInts(server).ToArray());
            }
        }

        [Fact]
        public async Task Value_larger_than_a_frame_is_dropped_and_reported_and_the_rest_goes_on()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer { MaxFrameBytes = 1100 })
            {
                var options = OptionsFor(server);
                options.PackageCollectPeriod = TimeSpan.FromHours(1);

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);
                    var text = collector.CreateStringSensor("session/huge");
                    var number = collector.CreateIntSensor("session/small");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/small"), "The session never came up.");

                    number.AddValue(1);
                    text.AddValue(new string('x', 5000));
                    number.AddValue(2);

                    await collector.Stop();
                }

                Assert.Equal(new[] { 1, 2 }, DeliveredInts(server).ToArray());
                Assert.DoesNotContain(server.Requests, r => r.Frame.Length > 1100);
            }

            Assert.Contains(logger.Errors, e => e.Contains("session/huge") && e.Contains("larger than a collector session frame"));
            Assert.Contains(logger.Errors, e => e.Contains("1 value(s) dropped from the full send queue"));
        }

        [Fact]
        public async Task Failed_frame_does_not_bring_back_what_was_delivered_or_dropped_before_it()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer { MaxFrameBytes = 1100 })
            {
                // Everything queues up while the upgrade is refused, so it leaves as one package
                // of several frames: 30 small values and one too large for a frame. A frame of
                // 1100 bytes holds at most seven of the small ones, so the large one, ninth in
                // the package, is dropped while the SECOND frame is filled, the frame that fails:
                // it is settled by nothing but its own drop.
                server.RefuseUpgrades(503);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var number = collector.CreateIntSensor("session/partial");
                    var text = collector.CreateStringSensor("session/partial-huge");
                    await collector.Start();

                    for (var i = 0; i < 8; i++)
                        number.AddValue(i);

                    text.AddValue(new string('x', 5000));

                    for (var i = 8; i < 30; i++)
                        number.AddValue(i);

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 1));

                    // The first list frame of the package is accepted, the second is answered 503.
                    server.FailListFramesAfter(1, 503);
                    server.AcceptUpgrades();

                    Assert.True(await WaitForAsync(() => server.Requests.Any(r => r.Op == "list" && r.AckStatus == 503)), "No list frame was refused.");

                    server.FailListFramesAfter(-1, 503);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Count >= 30), "The rest of the package never arrived.");

                    await collector.Stop();
                }

                // Every value once: the frame that was accepted before the failure did not go again.
                Assert.Equal(Enumerable.Range(0, 30).ToArray(), DeliveredInts(server).OrderBy(v => v).ToArray());
            }

            // The value that cannot be sent was dropped, and counted, once, not once per attempt:
            // it was settled by the pass that failed and did not come back with the rest.
            Assert.Single(logger.Errors.Where(e => e.Contains("session/partial-huge") && e.Contains("larger than a collector session frame")));
            Assert.Contains(logger.Errors, e => e.Contains("1 value(s) dropped from the full send queue"));
        }

        [Fact]
        public async Task Frames_filled_to_the_brim_still_fit_the_limit()
        {
            const int limit = 1100;
            const int count = 600;

            using (var server = new ScriptedSessionServer { MaxFrameBytes = limit })
            {
                var options = OptionsFor(server);
                options.PackageCollectPeriod = TimeSpan.FromHours(1); // the stop drain sends them

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateStringSensor("session/brim");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/brim"), "The session never came up.");

                    // Values of every length from 0 to 96 bytes, over and over: the frames are
                    // cut at about a hundred different fill levels, many of them within a few
                    // bytes of the room. A frame that counted its envelope or its separators
                    // wrong goes over the limit in one of them.
                    for (var i = 0; i < count; i++)
                        sensor.AddValue(new string('x', i % 97));

                    await collector.Stop();
                }

                var frames = server.Requests.Where(r => r.Op == "list").ToList();
                var largest = frames.Max(f => System.Text.Encoding.UTF8.GetByteCount(f.Frame));

                Assert.True(largest <= limit, $"A frame of {largest} bytes went out against a limit of {limit}.");
                Assert.Equal(count, server.AcceptedElements("list").Count(e => e.Contains("session/brim")));
            }
        }

        [Fact]
        public async Task Stop_in_the_middle_of_a_split_package_does_not_send_its_delivered_frames_again()
        {
            using (var server = new ScriptedSessionServer { MaxFrameBytes = 1100 })
            {
                server.RefuseUpgrades(503);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var number = collector.CreateIntSensor("session/stop-split");
                    await collector.Start();

                    for (var i = 0; i < 30; i++)
                        number.AddValue(i);

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 1));

                    // The first list frame is acknowledged, the second is left without an answer:
                    // the stop below finds the send waiting in the middle of the package.
                    server.FailListFramesAfter(1, 0);
                    server.AcceptUpgrades();

                    Assert.True(await WaitForAsync(() => server.Requests.Count(r => r.Op == "list") >= 2), "The second frame was never sent.");

                    server.FailListFramesAfter(-1, 0);

                    await collector.Stop();
                }

                // The drain delivered the rest, and the frame that was acknowledged before the stop
                // did not go a second time.
                Assert.Equal(Enumerable.Range(0, 30).ToArray(), DeliveredInts(server).OrderBy(v => v).ToArray());
            }
        }

        [Fact]
        public async Task Server_that_takes_the_upgrade_and_never_acknowledges_is_reported_as_an_error()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.RequestTimeout = TimeSpan.FromMilliseconds(200);

                server.StaySilentFor(int.MaxValue);

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/never-acked");
                    await collector.Start();

                    sensor.AddValue(1);

                    // Each session comes up, gets no ack, and is closed: three of them and counting.
                    Assert.True(await WaitForAsync(() => server.Sessions >= 3), "The session was not reopened.");

                    await collector.Stop();
                }
            }

            // Sessions that open and end at once deliver nothing; that is an error, said once.
            Assert.Single(logger.Errors.Where(e => e.Contains("ended early") && e.Contains("no ack")));
        }

        [Fact]
        public async Task Request_without_an_ack_is_an_error_on_a_session_of_any_age()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.RequestTimeout = TimeSpan.FromMilliseconds(200);

                // Every session counts as one that had lived long: what a request timeout of a
                // minute or more makes of each session that ends for a missing ack.
                options.SessionTimings.StableSession = TimeSpan.Zero;

                server.StaySilentFor(int.MaxValue);

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/never-acked-late");
                    await collector.Start();

                    sensor.AddValue(1);

                    Assert.True(await WaitForAsync(() => server.Sessions >= 3), "The session was not reopened.");

                    await collector.Stop();
                }
            }

            Assert.DoesNotContain(logger.Errors, e => e.Contains("ended early"));
            Assert.Single(logger.Errors.Where(e => e.Contains("was reset") && e.Contains("no ack")));
        }

        // --- a request without an ack ---

        [Fact]
        public async Task Request_without_an_ack_closes_the_session_and_is_sent_again_on_the_next_one()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.RequestTimeout = TimeSpan.FromMilliseconds(400);

                using (var collector = new DataCollector(options))
                {
                    await collector.Start();

                    Assert.True(await WaitForAsync(() => server.Sessions == 1), "The session never came up.");

                    // The next request, the registration of the sensor below, gets no ack.
                    server.StaySilentFor(1);

                    var sensor = collector.CreateIntSensor("session/no-ack");

                    // The value only when the registration has taken the silent slot: the two
                    // travel on queues of their own, and the value could be first otherwise.
                    Assert.True(await WaitForAsync(() => server.Requests.Any(r => r.Op == "commands" && r.Body.Contains("session/no-ack") && r.AckStatus == 0)),
                                "The registration was never sent.");

                    sensor.AddValue(7);

                    Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "commands" && r.Body.Contains("session/no-ack"))),
                                "The registration was not sent again.");

                    await collector.Stop();
                }

                var registrations = server.Requests.Where(r => r.Op == "commands" && r.Body.Contains("session/no-ack")).ToList();

                Assert.Equal(0, registrations[0].AckStatus);
                Assert.Equal(1, registrations[0].Session);
                // Closing the session is what makes a late ack impossible: the repeat is on a new one.
                Assert.True(registrations.Last().Session > 1, "The request was repeated on the session that had gone silent.");
                Assert.Equal(new[] { 7 }, DeliveredInts(server).Distinct().ToArray());
            }
        }

        [Fact]
        public async Task Request_that_fails_after_its_session_ended_does_not_end_the_next_session()
        {
            using (var server = new ScriptedSessionServer())
            using (var sender = new SessionDataSender(OptionsFor(server), new RecordingLogger()))
            {
                var connection = sender.Connection;

                sender.Start();

                Assert.True(await WaitForAsync(() => connection.SessionsOpened == 1), "The session never came up.");

                // The write lock of the connection. While the test holds it, a request has taken
                // its session and waits to write its frame: the place where a request can
                // outlive its session.
                var sendLock = (SemaphoreSlim)typeof(SessionConnection)
                    .GetField("_sendLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(connection);

                await sendLock.WaitAsync();

                Task<SessionAck> late;
                Task resetOfSecond;

                try
                {
                    // Runs up to its wait for the lock before it returns: the request is on session 1.
                    late = connection.RequestAsync(SessionProtocol.OpList, "[]", Timeout, CancellationToken.None);

                    Assert.False(late.IsCompleted, "The request did not wait for the write lock.");

                    // Session 1 ends and session 2 comes up, with the request still waiting. The
                    // collector ends it itself, by a stop that does not linger and a start: that
                    // aborts the socket of session 1 on this side and writes nothing, so it does
                    // not need the lock the test holds and does not depend on how the platform
                    // tells a client that its server is gone. (A session cut by the scripted
                    // server would not do: where its connection stays open underneath, as it
                    // does on Linux, the collector learns of the cut only from a write, and no
                    // write gets past the lock.)
                    await sender.StopAsync(terminal: true);
                    sender.Start();

                    Assert.True(await WaitForAsync(() => connection.SessionsOpened == 2), "The second session never came up.");

                    // Taken while the request still waits: it says whether session 2 was asked
                    // to end, whatever the loop has made of that by the time it is read.
                    resetOfSecond = connection.ResetOfCurrentSession;

                    Assert.NotNull(resetOfSecond);
                }
                finally
                {
                    sendLock.Release();
                }

                Assert.Same(late, await Task.WhenAny(late, Task.Delay(Timeout)));

                var answer = await late;

                // The request went the way of a failed write, on the socket of the session that
                // is gone: the one way a request asks for a reset long after its session ended.
                Assert.False(answer.Acked);
                Assert.StartsWith("sending failed", answer.Error);

                Assert.False(resetOfSecond.IsCompleted, "A request of the first session asked for the second session to be ended.");

                // And the second session serves the next request.
                var next = connection.RequestAsync(SessionProtocol.OpList, "[]", Timeout, CancellationToken.None);

                Assert.Same(next, await Task.WhenAny(next, Task.Delay(Timeout)));
                Assert.True((await next).IsSuccess, "The second session did not serve a request.");
                Assert.Equal(2, server.Sessions);
            }
        }

        [Fact]
        public async Task Sender_the_host_disposed_is_not_started_and_start_says_so()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);

                    // The sender the collector created is public as the DataSender of its
                    // options, and it is disposable: the one way a sender is disposed before
                    // the collector that owns it starts.
                    options.DataSender.Dispose();

                    await collector.Start();

                    Assert.Single(logger.Errors.Where(e => e.Contains("was disposed and cannot be started again")));
                    Assert.False(((SessionDataSender)options.DataSender).Connection.IsUp);

                    await collector.Stop();
                }

                Assert.Equal(0, server.Sessions);
            }
        }

        [Fact]
        public async Task Dropped_session_is_reopened_and_what_was_queued_meanwhile_arrives()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/dropped");
                    await collector.Start();

                    sensor.AddValue(1);
                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(1)), "The first value never arrived.");

                    server.RefuseUpgrades(503);
                    server.DropSessions();

                    sensor.AddValue(2);
                    sensor.AddValue(3);

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 2), "The collector did not try to reopen the session.");

                    server.AcceptUpgrades();

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(3)), "The values queued during the outage never arrived.");

                    await collector.Stop();
                }

                Assert.Equal(new[] { 1, 2, 3 }, DeliveredInts(server).Distinct().OrderBy(v => v).ToArray());
                Assert.True(server.Sessions >= 2);
                Assert.Empty(server.HttpRequests);
            }
        }

        // --- heartbeat ---

        [Fact]
        public async Task Idle_session_sends_heartbeats_and_stays_up()
        {
            // Two seconds, not one: the session is given up after two and a half heartbeats
            // without a frame, and a pong that is a second late must not look like that.
            using (var server = new ScriptedSessionServer { HeartbeatSeconds = 2 })
            using (var sender = new SessionDataSender(OptionsFor(server), new LoggerManager()))
            {
                sender.Start();

                Assert.True(await WaitForAsync(() => server.Pings >= 3), "An idle session sent no heartbeats.");
                Assert.Equal(1, server.Sessions);
                Assert.True(sender.Connection.IsUp);

                await sender.StopAsync(terminal: false);
            }
        }

        [Fact]
        public async Task Session_whose_server_went_silent_is_given_up_and_reopened()
        {
            using (var server = new ScriptedSessionServer { HeartbeatSeconds = 1 })
            using (var sender = new SessionDataSender(OptionsFor(server), new LoggerManager()))
            {
                server.StopAnsweringPings();
                sender.Start();

                // Two and a half heartbeats without a frame from the server: 2.5 s here.
                Assert.True(await WaitForAsync(() => server.Sessions >= 2), "The silent session was never given up.");

                await sender.StopAsync(terminal: true);
            }
        }

        // --- what a status means ---

        [Fact]
        public async Task Registration_refused_with_4xx_on_the_session_is_final()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    await collector.Start();
                    Assert.True(await WaitForAsync(() => server.Sessions == 1));

                    server.AckWith("commands", 400);
                    collector.CreateIntSensor("session/rejected");

                    Assert.True(await WaitForAsync(() => server.Requests.Any(r => r.Op == "commands" && r.Body.Contains("session/rejected"))),
                                "The registration was never sent.");

                    // A later registration is the clock: a re-queued one would ride with it.
                    server.AckWith("commands", 200);
                    collector.CreateIntSensor("session/later");

                    Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "commands" && r.Body.Contains("session/later"))),
                                "The later registration never arrived.");

                    await collector.Stop();
                }

                Assert.Single(server.Requests.Where(r => r.Op == "commands" && r.Body.Contains("session/rejected")));
            }
        }

        [Theory]
        [InlineData(400)]
        [InlineData(503)]
        public async Task Value_batch_refused_on_the_session_is_sent_again(int status)
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/again");
                    await collector.Start();
                    Assert.True(await WaitForAsync(() => server.Sessions == 1));

                    server.AckWith("list", status);
                    sensor.AddValue(9);

                    Assert.True(await WaitForAsync(() => server.Requests.Count(r => r.Op == "list" && r.Body.Contains("session/again")) >= 2),
                                $"A value batch acknowledged {status} was not sent again.");

                    server.AckWith("list", 200);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(9)), "The value never arrived.");

                    await collector.Stop();
                }
            }
        }

        // --- files ---

        // #1660 step 2b: a file value is one element of a list frame, the bytes the HTTP /list
        // route would take for it.
        [Fact]
        public async Task File_value_is_one_element_of_a_list_frame_and_the_http_list_body_byte_for_byte()
        {
            var file = new FileSensorValue
            {
                Path = "wire/file",
                Value = new List<byte> { 104, 105, 0, 255 },
                Name = "report",
                Extension = "txt",
                Comment = "first",
                Time = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc),
            };

            using (var server = new ScriptedSessionServer())
            using (var sender = new SessionDataSender(OptionsFor(server), new LoggerManager()))
            {
                sender.Start();

                Assert.True(await WaitForAsync(() => sender.Connection.IsUp), "The session never came up.");

                var sent = await sender.SendFileAsync(file, CancellationToken.None);

                await sender.StopAsync(terminal: false);

                Assert.True(sent.IsSuccess, sent.Error);

                var request = server.Requests.Single();

                Assert.Equal("list", request.Op);
                Assert.Equal(new HttpRequest<SensorValueBase>(new SensorValueBase[] { file }, "unused").Content, request.Body);
                Assert.Contains("\"Type\":6,\"Extension\":\"txt\",\"Name\":\"report\",\"Value\":[104,105,0,255]", request.Body);
            }
        }

        [Fact]
        public async Task File_value_is_delivered_on_the_session_with_the_values_around_it()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var file = collector.CreateFileSensor("session/file", "report");
                    var number = collector.CreateIntSensor("session/after-file");
                    await collector.Start();

                    file.AddValue("content", SensorStatus.Ok, "note");
                    number.AddValue(5);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(5)), "The value after the file never arrived.");
                    Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "list" && r.Body.Contains("\"Value\":[99,111,110,116,101,110,116],\"Comment\":\"note\""))),
                                "The file value never arrived in a list frame.");

                    await collector.Stop();
                }

                Assert.Empty(server.HttpRequests);
            }
        }

        [Fact]
        public async Task File_larger_than_a_frame_is_dropped_reported_and_counted()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer { MaxFrameBytes = 2048 })
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var file = collector.CreateFileSensor("session/huge-file", "report");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/huge-file"), "The session never came up.");

                    // 1000 bytes of 'x' are 4000 bytes of JSON: no frame of 2048 bytes holds them.
                    file.AddValue(new string('x', 1000));
                    file.AddValue("after");

                    Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "list" && r.Body.Contains("\"Value\":[97,102,116,101,114]"))),
                                "The file after the dropped one never arrived.");

                    await collector.Stop();
                }

                Assert.DoesNotContain(server.Requests, r => r.Frame.Contains("120,120,120,120"));
                Assert.DoesNotContain(server.Requests, r => r.Frame.Length > 2048);
            }

            Assert.Contains(logger.Errors, e => e.Contains("session/huge-file") && e.Contains("larger than a collector session frame"));
            Assert.Contains(logger.Errors, e => e.Contains("1 value(s) dropped from the full send queue"));
        }

        [Fact]
        public async Task File_value_waits_for_the_session_and_arrives_when_it_is_up()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                server.RefuseUpgrades(503);

                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var file = collector.CreateFileSensor("session/file-waits", "report");
                    await collector.Start();

                    file.AddValue("queued");

                    Assert.True(await WaitForAsync(() => server.RefusedUpgrades >= 2), "The collector did not try to open a session.");
                    Assert.Empty(server.Requests);

                    server.AcceptUpgrades();

                    Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "list" && r.Body.Contains("\"Value\":[113,117,101,117,101,100]"))),
                                "The file value never arrived once the session was up.");

                    await collector.Stop();
                }

                Assert.Empty(server.HttpRequests);
            }

            // Waiting for a session is not an error of the file queue: the session reports itself.
            Assert.DoesNotContain(logger.Errors, e => e.Contains("Queue: File"));
        }

        // --- directive push (#1660 step 5) ---

        // A directive the server pushes reaches the collector's directive receiver, whole and in
        // the order of the pushes, as the X-Hsm-Directive headers of an HTTP answer do. A push whose
        // body is not a string and a push op the collector does not know change nothing, and the
        // session carries on.
        [Fact]
        public async Task Pushed_directives_reach_the_receiver_in_order()
        {
            var received = new System.Collections.Concurrent.ConcurrentQueue<string>();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.ExtraRequestHeaders["X-Agent-Version"] = "0.5.1";

                using (var collector = new DataCollector(options))
                {
                    var directives = ((SessionDataSender)options.DataSender).Directives;
                    directives.Observer = received.Enqueue;

                    var sensor = collector.CreateIntSensor("session/directives");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/directives"), "The session never came up.");
                    Assert.Equal("0.5.1", server.UpgradeHeaders["X-Agent-Version"]);

                    Assert.Equal(1, server.PushDirective("update-available:0.6.0"));
                    server.Push("{\"op\":\"directive\",\"body\":{\"not\":\"a string\"}}");
                    server.Push("{\"op\":\"news\",\"body\":\"sensor-disable:disk\"}");
                    server.PushDirective("sensor-disable:network");

                    Assert.True(await WaitForAsync(() => received.Count >= 2), "The pushed directives never reached the receiver.");

                    sensor.AddValue(17);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(17)), "The value after the pushes never arrived.");

                    await collector.Stop();

                    Assert.Equal(new[] { "update-available:0.6.0", "sensor-disable:network" }, received.ToArray());
                    Assert.Equal(2, directives.ReceivedCount);
                    Assert.Equal(1, server.Sessions);
                }
            }
        }

        // --- stop ---

        [Fact]
        public async Task Collector_started_through_the_obsolete_initialize_opens_its_session()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/initialize");

#pragma warning disable CS0618 // the entry point under test
                    collector.Initialize(false);
#pragma warning restore CS0618

                    sensor.AddValue(9);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(9)), "A collector started through Initialize delivered nothing.");

                    await collector.Stop();
                }

                Assert.Empty(server.HttpRequests);
            }
        }

        [Fact]
        public async Task Close_from_the_server_is_answered_with_a_close_and_the_session_is_reopened()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/server-close");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/server-close"), "The session never came up.");

                    // A server that shuts down: "going away", and it waits for the answer.
                    server.CloseSessions(1001);

                    // Answered with the code it was closed with, as the native collector does.
                    Assert.True(await WaitForAsync(() => server.Closes.Count >= 1), "The collector dropped the session instead of answering the close.");
                    Assert.Equal(1001, server.Closes[0]);

                    sensor.AddValue(3);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(3)), "The value after the close never arrived.");
                    Assert.True(server.Sessions >= 2, "The session was not opened again.");

                    await collector.Stop();
                }
            }
        }

        [Fact]
        public async Task Hello_with_limits_the_collector_cannot_work_with_is_refused_and_reported()
        {
            var logger = new RecordingLogger();

            // Below the smallest frame limit a session may state.
            using (var server = new ScriptedSessionServer { MaxFrameBytes = 512 })
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/hello");
                    await collector.Start();

                    sensor.AddValue(4);

                    Assert.True(await WaitForAsync(() => server.Sessions >= 2), "The collector did not try again after a hello it cannot work with.");
                    Assert.Empty(server.Requests);

                    server.MaxFrameBytes = 4096;

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(4)), "The value never arrived on a session with a usable hello.");

                    await collector.Stop();
                }
            }

            Assert.Contains(logger.Errors, e => e.Contains("limits this collector cannot work with"));
        }

        [Fact]
        public async Task Second_collector_from_the_same_options_does_not_close_the_session_of_the_first()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                using (var first = new DataCollector(options))
                using (var second = new DataCollector(options))
                {
                    var sensor = first.CreateIntSensor("session/owner");

                    await first.Start();
                    await second.Start();

                    Assert.True(await RegisteredAsync(server, "session/owner"), "The session never came up.");

                    // The second collector shares the sender of the first and does not own it.
                    await second.Stop();

                    sensor.AddValue(21);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(21)), "The first collector lost its session to the stop of the second.");
                    Assert.Equal(1, server.Sessions);
                    Assert.Empty(server.Closes);

                    await first.Stop();
                }
            }
        }

        [Fact]
        public async Task Disposing_the_second_collector_leaves_the_session_of_the_first_up()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                using (var first = new DataCollector(options))
                {
                    var sensor = first.CreateIntSensor("session/owner-outlives");

                    await first.Start();

                    Assert.True(await RegisteredAsync(server, "session/owner-outlives"), "The session never came up.");

                    // Created from the same options, started and disposed while the first runs.
                    using (var second = new DataCollector(options))
                        await second.Start();

                    sensor.AddValue(34);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(34)), "The first collector lost its session to the dispose of the second.");
                    Assert.Equal(1, server.Sessions);
                    Assert.Empty(server.Closes);

                    await first.Stop();
                }
            }
        }

        [Fact]
        public async Task Second_collector_says_so_when_the_session_it_shares_is_not_running()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);

                // The first collector owns the session and is never started: there is no session,
                // and the transport, which reports to its owner, has nothing to say.
                using (var first = new DataCollector(options))
                using (var second = new DataCollector(options))
                {
                    second.AddCustomLogger(logger);

                    var sensor = second.CreateIntSensor("session/not-the-owner");

                    await second.Start();

                    sensor.AddValue(5);

                    Assert.True(await WaitForAsync(() => logger.Errors.Any(e => e.Contains("Queue: Data") && e.Contains("Failed to send"))),
                                "A collector whose values go nowhere said nothing: " + string.Join(" | ", logger.Errors));

                    await second.Stop();
                }

                Assert.Equal(0, server.Sessions);
            }
        }

        [Fact]
        public async Task Send_file_on_a_session_delivers_the_file()
        {
            var path = System.IO.Path.GetTempFileName();

            try
            {
                System.IO.File.WriteAllText(path, "content");

                using (var server = new ScriptedSessionServer())
                {
                    using (var collector = new DataCollector(OptionsFor(server)))
                    {
                        var file = collector.CreateFileSensor("session/send-file", "report");
                        await collector.Start();

                        Assert.True(await file.SendFile(path), "SendFile did not take the file.");

                        Assert.True(await WaitForAsync(() => server.Accepted.Any(r => r.Op == "list" && r.Body.Contains("\"Value\":[99,111,110,116,101,110,116]"))),
                                    "The file never arrived in a list frame.");

                        await collector.Stop();
                    }

                    Assert.Empty(server.HttpRequests);
                }
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public async Task Request_that_is_no_upgrade_is_on_the_record_of_the_scripted_server()
        {
            // What "no HTTP request on a session" is checked against must itself be seen to work.
            using (var server = new ScriptedSessionServer())
            using (var client = new System.Net.Http.HttpClient())
            {
                using (var response = await client.GetAsync($"{server.ServerAddress}:{server.Port}/api/sensors/list"))
                    Assert.Equal(404, (int)response.StatusCode);

                Assert.Equal(new[] { "GET /api/sensors/list" }, server.HttpRequests.ToArray());
            }
        }

        [Fact]
        public async Task Untrusted_certificate_setting_does_not_stand_in_the_way_of_a_plaintext_session()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.AllowUntrustedServerCertificate = true; // left on from a development config

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateIntSensor("session/plaintext");
                    await collector.Start();

                    sensor.AddValue(8);

                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(8)), "A plaintext session was refused over a certificate setting.");

                    await collector.Stop();
                }
            }
        }

        [Fact]
        public async Task Stop_delivers_what_is_queued_and_closes_the_session_normally()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.PackageCollectPeriod = TimeSpan.FromHours(1);

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateIntSensor("session/stop");
                    await collector.Start();

                    Assert.True(await RegisteredAsync(server, "session/stop"), "The session never came up.");

                    sensor.AddValue(11);
                    sensor.AddValue(12);

                    await collector.Stop();
                }

                Assert.Equal(new[] { 11, 12 }, DeliveredInts(server).ToArray());

                // Stop waits a moment for the server's answer to its close and no longer.
                Assert.True(await WaitForAsync(() => server.Closes.Count == 1), "The collector sent no close frame.");
                Assert.Equal(new[] { 1000 }, server.Closes.ToArray());
            }
        }

        [Fact]
        public async Task Restarted_collector_opens_a_new_session()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var sensor = collector.CreateIntSensor("session/restart");

                    await collector.Start();
                    sensor.AddValue(1);
                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(1)));
                    await collector.Stop();

                    await collector.Start();
                    sensor.AddValue(2);
                    Assert.True(await WaitForAsync(() => DeliveredInts(server).Contains(2)), "Nothing arrived after the restart.");
                    await collector.Stop();
                }

                Assert.Equal(2, server.Sessions);
            }
        }

        // --- the setting ---

        [Fact]
        public void Session_is_the_default_transport()
        {
            var options = new CollectorOptions();

            Assert.Equal(CollectorTransport.Session, options.Transport);
            Assert.False(options.TransportWasSet);

            options.Transport = CollectorTransport.Http;
            Assert.True(options.TransportWasSet);
        }

        // ADR-0019: a collector that names no transport opens a session.
        [Fact]
        public async Task A_collector_with_the_default_transport_sends_on_a_session()
        {
            using (var server = new ScriptedSessionServer())
            {
                var options = new CollectorOptions
                {
                    ServerAddress = server.ServerAddress,
                    Port = server.Port,
                    AccessKey = "session-key",
                    ClientName = "session-client",
                    AllowPlaintextTransport = true,
                    PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
                    RequestTimeout = TimeSpan.FromSeconds(10),
                };

                using (var collector = new DataCollector(options))
                {
                    Assert.IsType<SessionDataSender>(options.DataSender);

                    var sensor = collector.CreateIntSensor("session/default");
                    await collector.Start();

                    sensor.AddValue(1);

                    Assert.True(await WaitForAsync(() => server.Sessions >= 1), "The default transport opened no session.");

                    await collector.Stop();
                }
            }
        }

        // A collector that names no transport next to a custom sender is not told the setting
        // has no effect: it set nothing.
        [Fact]
        public async Task The_default_transport_next_to_a_custom_sender_is_not_reported()
        {
            var logger = new RecordingLogger();
            var options = new CollectorOptions { AccessKey = "session-key", DataSender = new AcceptingSender() };

            using (var collector = new DataCollector(options))
            {
                collector.AddCustomLogger(logger);
                await collector.Start();
                await collector.Stop();
            }

            Assert.DoesNotContain(logger.Infos, i => i.Contains("has no effect"));
        }

#if !NET6_0_OR_GREATER
        // On .NET Framework a session cannot honour AllowUntrustedServerCertificate. The default
        // is then HTTP, and Start says why; a Session the caller set stays a session (ADR-0019).
        [Fact]
        public async Task The_default_is_http_where_no_session_can_exist_and_start_says_why()
        {
            var logger = new RecordingLogger();
            var options = new CollectorOptions
            {
                ServerAddress = "127.0.0.1",
                AccessKey = "session-key",
                Port = 1,
                AllowUntrustedServerCertificate = true,
                RequestTimeout = TimeSpan.FromSeconds(1),
            };

            using (var collector = new DataCollector(options))
            {
                Assert.IsType<HSMDataCollector.Client.HsmHttpsClient>(options.DataSender);

                collector.AddCustomLogger(logger);
                await collector.Start();
                await collector.Stop();
            }

            Assert.Single(logger.Infos.Where(i => i.Contains("The collector sends over HTTP") && i.Contains("AllowUntrustedServerCertificate")));

            var explicitSession = new CollectorOptions
            {
                ServerAddress = "127.0.0.1",
                AccessKey = "session-key",
                Port = 1,
                AllowUntrustedServerCertificate = true,
                Transport = CollectorTransport.Session,
            };

            using (new DataCollector(explicitSession))
                Assert.IsType<SessionDataSender>(explicitSession.DataSender);
        }
#endif

        [Fact]
        public async Task Session_setting_next_to_a_custom_sender_has_no_effect_and_start_says_so()
        {
            var logger = new RecordingLogger();
            var custom = new AcceptingSender();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.DataSender = custom;

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/custom");
                    await collector.Start();

                    sensor.AddValue(1);

                    Assert.True(await WaitForAsync(() => custom.DataPackages >= 1), "The custom sender was not used.");

                    await collector.Stop();
                }

                Assert.Equal(0, server.Sessions);
            }

            Assert.Single(logger.Infos.Where(i => i.Contains("Transport = Session has no effect")));
        }

        [Fact]
        public async Task Session_without_the_service_alive_sensor_is_warned_about()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                collector.AddCustomLogger(logger);
                await collector.Start();
                await collector.Stop();
            }

            Assert.Single(logger.Infos.Where(i => i.Contains("Service alive sensor (AddCollectorAlive) is not registered")));
        }

        [Fact]
        public async Task Test_connection_on_a_session_opens_a_session_and_not_an_http_request()
        {
            using (var server = new ScriptedSessionServer())
            {
                using (var collector = new DataCollector(OptionsFor(server)))
                {
                    var reachable = await collector.TestConnection();

                    Assert.True(reachable.IsOk, reachable.Error);

                    server.RefuseUpgrades(404);

                    var refused = await collector.TestConnection();

                    Assert.False(refused.IsOk);
                    Assert.False(string.IsNullOrEmpty(refused.Error));
                }

                Assert.Empty(server.HttpRequests);
            }
        }

#if !NET5_0_OR_GREATER
        [Fact]
        public async Task Untrusted_certificate_setting_makes_the_session_unavailable_on_net_framework()
        {
            var logger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.AllowUntrustedServerCertificate = true;
                // A TLS session: only there is a certificate the setting could excuse.
                options.ServerAddress = "https://localhost";
                options.AllowPlaintextTransport = false;

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);
                    var sensor = collector.CreateIntSensor("session/unavailable");

                    // Start completes and does not throw; one error names the cause.
                    await collector.Start();
                    sensor.AddValue(1);

                    await Task.Delay(300);
                    await collector.Stop();
                }

                Assert.Equal(0, server.Sessions);
                Assert.Equal(0, server.RefusedUpgrades);
                Assert.Empty(server.HttpRequests);
            }

            Assert.Single(logger.Errors.Where(e => e.Contains("collector session transport is unavailable")));
        }

        [Fact]
        public async Task Unavailable_session_is_reported_on_every_start()
        {
            var logger = new RecordingLogger();
            var secondLogger = new RecordingLogger();

            using (var server = new ScriptedSessionServer())
            {
                var options = OptionsFor(server);
                options.AllowUntrustedServerCertificate = true;
                options.ServerAddress = "https://localhost";
                options.AllowPlaintextTransport = false;
                // No window: every report is written, so the lines are the reports of the sender.
                options.ExceptionDeduplicatorWindow = TimeSpan.Zero;

                using (var collector = new DataCollector(options))
                {
                    collector.AddCustomLogger(logger);

                    await collector.Start();
                    await collector.Stop();

                    Assert.Single(logger.Errors.Where(e => e.Contains("collector session transport is unavailable")));

                    // A host that starts its collector again is told again why nothing is delivered.
                    await collector.Start();

                    Assert.Equal(2, logger.Errors.Count(e => e.Contains("collector session transport is unavailable")));

                    // A second collector from the same options shares the sender and does not
                    // start it: it adds no report, to its own log or to the owner's.
                    using (var second = new DataCollector(options))
                    {
                        second.AddCustomLogger(secondLogger);

                        await second.Start();
                        await second.Stop();
                    }

                    await collector.Stop();
                }

                Assert.Equal(0, server.Sessions);
            }

            Assert.Equal(2, logger.Errors.Count(e => e.Contains("collector session transport is unavailable")));
            Assert.DoesNotContain(secondLogger.Errors, e => e.Contains("collector session transport is unavailable"));
        }
#endif


        // Start does not wait for the session. An accepted registration is the proof that the
        // collector has one: what a test queues after it is drained by Stop over the session.
        private static Task<bool> RegisteredAsync(ScriptedSessionServer server, string path) =>
            WaitForAsync(() => server.Accepted.Any(r => r.Op == "commands" && r.Body.Contains(path)));

        // The int values of the accepted list frames, in the order they arrived.
        private static List<int> DeliveredInts(ScriptedSessionServer server)
        {
            var values = new List<int>();

            foreach (var element in server.AcceptedElements("list"))
            {
                using (var document = System.Text.Json.JsonDocument.Parse(element))
                {
                    var root = document.RootElement;

                    if (root.TryGetProperty("Type", out var type) && type.GetInt32() == (int)SensorType.IntSensor
                        && root.TryGetProperty("Value", out var value) && value.TryGetInt32(out var number))
                        values.Add(number);
                }
            }

            return values;
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

        private sealed class AcceptingSender : IDataSender
        {
            private int _dataPackages;

            internal int DataPackages => Volatile.Read(ref _dataPackages);

            public void Dispose() { }

            public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

            public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
            {
                Interlocked.Increment(ref _dataPackages);
                return Accepted();
            }

            public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => Accepted();

            public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Accepted();

            private static ValueTask<PackageSendingInfo> Accepted() =>
                new ValueTask<PackageSendingInfo>(new PackageSendingInfo(1, 200, null));
        }

        private sealed class RecordingLogger : ICollectorLogger
        {
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _infos = new List<string>();

            internal List<string> Errors
            {
                get
                {
                    lock (_errors)
                        return _errors.ToList();
                }
            }

            internal List<string> Infos
            {
                get
                {
                    lock (_infos)
                        return _infos.ToList();
                }
            }

            public void Debug(string message) { }

            public void Info(string message)
            {
                lock (_infos)
                    _infos.Add(message);
            }

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
