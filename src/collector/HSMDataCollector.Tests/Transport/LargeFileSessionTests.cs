#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDataCollector.Client.Session;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Tests.LargeFiles;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// Large files through a real collector over a real WebSocket (#1888): the file connection as the wire contract says,
    /// against the scripted server's file side. The upload logic itself is tested in memory in <c>LargeFiles/</c>.
    /// </summary>
    public sealed class LargeFileSessionTests : IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "hsm-large-session-" + Guid.NewGuid().ToString("N").Substring(0, 12));

        public LargeFileSessionTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            LargeFileHarness.Collect();

            try { Directory.Delete(_root, true); }
            catch (Exception) { }
        }

        private CollectorOptions OptionsFor(ScriptedSessionServer server) => new CollectorOptions
        {
            ServerAddress = server.ServerAddress,
            Port = server.Port,
            AccessKey = "large-file-key",
            ComputerName = "host",
            Module = "module",
            AllowPlaintextTransport = true,
            Transport = CollectorTransport.Session,
            PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(10),
            DataFolder = Path.Combine(_root, "data"),
            SessionTimings = new SessionTimings { MinBackoff = TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromMilliseconds(100) },
        };

        private string WriteFile(string name, int size, uint seed = 3)
        {
            var path = Path.Combine(_root, name);

            File.WriteAllBytes(path, SeededContent.Make(size, seed));
            return path;
        }

        [Fact]
        public void A_handed_file_is_stored_over_the_file_connection_and_the_connection_closes_before_the_session()
        {
            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                server.AcceptFileUpgrades();

                var path = WriteFile("a.dmp", 65536 * 2 + 17);
                var content = File.ReadAllBytes(path);

                collector.Start().Wait();

                var task = collector.SendLargeFileAsync("dumps", path);

                Assert.True(task.Wait(Patience));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.True(task.Result.SourceDeleted);
                Assert.Equal(content, server.Files.StoredFile("a.dmp"));
                Assert.Equal("host/module/dumps", server.Files.Begins.Single().GetProperty("path").GetString());

                // The registration went on the session with the large file flag, before the begin.
                Assert.Contains(server.Requests, r => r.Op == "commands" && r.Body.Contains("\"Path\":\"host/module/dumps\"") && r.Body.Contains("\"DefaultAlertsOptions\":4"));
                Assert.Equal(server.SessionIds.Last(), server.FileUpgradeHeaders.Last()["X-Hsm-Session"]);

                collector.Stop().Wait();

                Assert.Contains(1000, server.FileCloses);
            }
        }

        [Fact]
        public void A_watched_folder_is_sent_through_the_collector()
        {
            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                server.AcceptFileUpgrades();

                var folder = Path.Combine(_root, "dumps");

                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "x.dmp"), SeededContent.Make(70000, 11));

                collector.Start().Wait();

                var watch = collector.WatchLargeFiles("dumps", folder, "*.dmp", new LargeFileOptions { SettleTime = TimeSpan.FromSeconds(1) });

                Assert.True(SpinWait.SpinUntil(() => watch.FilesStored == 1, Patience));
                Assert.Equal(SeededContent.Make(70000, 11), server.Files.StoredFile("x.dmp"));
                Assert.True(SpinWait.SpinUntil(() => !File.Exists(Path.Combine(folder, "x.dmp")), Patience));
            }
        }

        [Fact]
        public void A_server_without_file_connections_is_reported_and_the_file_waits()
        {
            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                server.RefuseFileUpgrades(404);

                var path = WriteFile("a.dmp", 1000);

                collector.Start().Wait();

                var task = collector.SendLargeFileAsync("dumps", path);

                Assert.True(SpinWait.SpinUntil(() => server.RefusedFileUpgrades >= 1, Patience));
                Assert.False(task.Wait(500));
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void An_answer_that_never_comes_is_a_lost_connection_and_the_upload_continues()
        {
            using (var server = new ScriptedSessionServer())
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                var path = WriteFile("a.dmp", 65536 * 3);
                var content = File.ReadAllBytes(path);

                server.AcceptFileUpgrades();
                server.Files.AnswerPieceNext(FileAnswer.Silent());
                collector.Start().Wait();

                var sender = (SessionDataSender)typeof(DataCollector).GetField("_dataSender", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(collector);

                sender.Files.AckDeadlineForTests = id => server.WhenFileRequestUnanswered(id);

                var task = collector.SendLargeFileAsync("dumps", path);

                Assert.True(task.Wait(Patience));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Equal(content, server.Files.StoredFile("a.dmp"));
                Assert.True(server.FileConnections >= 2);
            }
        }

        // A file value above a quarter of the session frame (65536 / 4) that still fits a frame: 20000 bytes of 'a' are 60000
        // bytes of JSON.
        private static int ListFramesWithTheValue(ScriptedSessionServer server) =>
            server.Accepted.Count(r => r.Op == "list" && r.Body.Contains("\"Value\":[97,97,97,97,97,97,97,97"));

        [Fact]
        public void A_routed_value_whose_registration_came_back_with_an_error_is_sent_in_a_list_frame_once()
        {
            using (var server = new ScriptedSessionServer { MaxFrameBytes = 65536 })
            using (var collector = new DataCollector(OptionsFor(server)))
            {
                server.AcceptFileUpgrades();
                server.RefuseRegistrationOf("host/module/session/quarter", "not this time");

                var file = collector.CreateFileSensor("session/quarter", "report");

                collector.Start().Wait();
                Assert.True(SpinWait.SpinUntil(() => server.Requests.Any(r => r.Op == "commands" && r.Body.Contains("session/quarter")), Patience));

                file.AddValue(new string('a', 20000));

                Assert.True(SpinWait.SpinUntil(() => ListFramesWithTheValue(server) == 1, Patience));
                Thread.Sleep(1000);

                Assert.Equal(1, ListFramesWithTheValue(server));
                Assert.Equal(1, collector.LargeFileService.RoutesTaken);
                Assert.Equal(0, server.Files.BeginCount("report.txt"));
            }
        }

        [Fact]
        public void A_routed_value_whose_registration_is_not_acknowledged_in_60_s_is_sent_in_a_list_frame_once()
        {
            var timings = new VirtualTimings();

            using (var server = new ScriptedSessionServer { MaxFrameBytes = 65536 })
            {
                var options = OptionsFor(server);

                options.LargeFileTimings = timings;

                using (var collector = new DataCollector(options))
                {
                    server.AcceptFileUpgrades();

                    // Every registration faults: it is never acknowledged.
                    server.AckWith("commands", 500);

                    var file = collector.CreateFileSensor("session/quarter", "report");

                    collector.Start().Wait();
                    Assert.True(SpinWait.SpinUntil(() => server.Requests.Any(r => r.Op == "commands" && r.Body.Contains("session/quarter")), Patience));

                    file.AddValue(new string('a', 20000));

                    Assert.True(SpinWait.SpinUntil(() => collector.LargeFileService != null && collector.LargeFileService.RoutesTaken == 1, Patience));

                    for (var i = 0; i < 30; i++)
                        timings.Step(collector.LargeFileService.Wake);

                    Assert.Equal(0, ListFramesWithTheValue(server));

                    for (var i = 0; i < 40 && ListFramesWithTheValue(server) == 0; i++)
                        timings.Step(collector.LargeFileService.Wake);

                    Assert.True(SpinWait.SpinUntil(() => ListFramesWithTheValue(server) == 1, Patience));
                    Thread.Sleep(1000);

                    Assert.Equal(1, ListFramesWithTheValue(server));
                    Assert.Equal(1, collector.LargeFileService.RoutesTaken);
                }
            }
        }

        [Fact]
        public void A_collector_on_http_cannot_send_large_files_and_says_so()
        {
            var options = new CollectorOptions { AccessKey = "k", ServerAddress = "https://localhost", Port = 44399, Transport = CollectorTransport.Http };

            using (var collector = new DataCollector(options))
            {
                var path = WriteFile("a.dmp", 10);

                var result = collector.SendLargeFileAsync("dumps", path).Result;
                var watch = collector.WatchLargeFiles("dumps", _root, "*.dmp");

                Assert.Equal(LargeFileOutcome.Unavailable, result.Outcome);
                Assert.Contains("HTTP", result.Reason);
                Assert.Equal(LargeFileWatchState.Unavailable, watch.State);
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void A_disposed_collector_answers_without_throwing()
        {
            using (var server = new ScriptedSessionServer())
            {
                var collector = new DataCollector(OptionsFor(server));

                collector.Dispose();

                Assert.Equal(LargeFileWatchState.Disposed, collector.WatchLargeFiles("dumps", _root, "*.dmp").State);
                Assert.Equal(LargeFileOutcome.Cancelled, collector.SendLargeFileAsync("dumps", WriteFile("a.dmp", 10)).Result.Outcome);
            }
        }
    }
}
