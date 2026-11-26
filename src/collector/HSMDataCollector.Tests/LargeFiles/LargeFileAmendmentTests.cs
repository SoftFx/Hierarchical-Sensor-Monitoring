#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>
    /// The owner's amendment (ADR-0022 points 20, 21) and the shared rules of the native review (#1888 design §13): secure
    /// marks, ordinary file values routed to the file connection, 1008, the retryAfter cap, files held open, stale records.
    /// </summary>
    public sealed class LargeFileAmendmentTests
    {
        private const int Piece = 65536;
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        private static FileSensorValue FileValue(int size, SensorStatus status = SensorStatus.Ok, string comment = null) => new FileSensorValue
        {
            Path = "host/module/report",
            Name = "report",
            Extension = "txt",
            Value = SeededContent.Make(size, 5).ToList(),
            Time = new DateTime(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc),
            Status = status,
            Comment = comment,
        };

        private static (LargeFileHarness Harness, List<FileSensorValue> Requeued, List<int> Dropped) Routing()
        {
            var h = new LargeFileHarness(Piece);
            var requeued = new List<FileSensorValue>();
            var dropped = new List<int>();

            h.StartWith((s, t) => new LargeFileService(t, null, s, LargeFileHarness.Registration, h.Errors.Enqueue, n => true, null, h.Timings,
                                                       n => { lock (dropped) dropped.Add(n); }, v => { lock (requeued) requeued.Add(v); }));

            return (h, requeued, dropped);
        }

        [Fact]
        public void A_handed_file_is_secure_unless_the_option_says_otherwise()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();

                Assert.True(h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None).Wait(Wait));
                Assert.True(h.Service.Send("dumps", h.WriteFile("b.dmp", 10), new LargeFileOptions { Secure = false }, CancellationToken.None).Wait(Wait));

                Assert.True(h.Store.Begins[0].GetProperty("secure").GetBoolean());
                Assert.False(h.Store.Begins[1].GetProperty("secure").GetBoolean());
                Assert.False(h.Store.Begins[0].TryGetProperty("status", out _));
            }
        }

        [Fact]
        public void A_routed_value_goes_on_the_file_connection_not_secure_with_its_status_comment_and_time()
        {
            var (h, requeued, dropped) = Routing();

            using (h)
            {
                var value = FileValue(Piece * 2 + 10, SensorStatus.Warning, "nightly");

                Assert.True(h.Service.Route(value));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));

                var begin = h.Store.Begins.Single();

                Assert.Equal("host/module/report", begin.GetProperty("path").GetString());
                Assert.False(begin.GetProperty("secure").GetBoolean());
                Assert.Equal(2, begin.GetProperty("status").GetInt32());
                Assert.Equal("nightly", begin.GetProperty("comment").GetString());
                Assert.Equal("2026-11-26T08:00:00.0000000Z", begin.GetProperty("modified").GetString());
                Assert.StartsWith("send:", begin.GetProperty("source").GetString());
                Assert.Equal(value.Value.ToArray(), h.Store.StoredFile("report.txt"));
                Assert.Empty(h.Transport.Registrations);
                Assert.Empty(requeued);
                Assert.Empty(dropped);
                Assert.Empty(Directory.Exists(h.DataFolder) ? Directory.GetFiles(h.DataFolder, "upload-*") : new string[0]);
            }
        }

        [Fact]
        public void Two_routed_values_of_one_name_and_size_are_two_files()
        {
            var (h, _, _) = Routing();

            using (h)
            {
                Assert.True(h.Service.Route(FileValue(Piece + 1)));
                Assert.True(h.Service.Route(FileValue(Piece + 1)));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 2));
            }
        }

        [Fact]
        public void A_routed_value_waits_for_its_sensors_registration()
        {
            var (h, _, dropped) = Routing();

            using (h)
            {
                bool? registered = null;

                h.Transport.Registration = _ => registered;
                Assert.True(h.Service.Route(FileValue(Piece + 1)));

                h.Steps(5);
                Assert.Equal(0, h.Store.BeginCount("report.txt"));

                registered = true;

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));
                Assert.Empty(dropped);
            }
        }

        [Fact]
        public void A_refused_routed_value_is_a_dropped_file_value()
        {
            var (h, _, dropped) = Routing();

            using (h)
            {
                h.Store.AnswerBeginNext(FileAnswer.CannotTake());
                Assert.True(h.Service.Route(FileValue(Piece + 1)));

                Assert.True(h.AdvanceUntil(() => { lock (dropped) return dropped.Sum() == 1; }));
                Assert.Equal(0, h.Dropped);
                Assert.Contains(h.Errors, e => e.Contains("report.txt") && e.Contains("host/module/report"));
            }
        }

        [Fact]
        public void Without_a_file_connection_routed_values_go_back_to_the_ordinary_path_for_the_session()
        {
            var (h, requeued, dropped) = Routing();

            using (h)
            {
                h.Transport.FakeChannel.RefuseOpens.Enqueue(404);

                var value = FileValue(Piece + 1);

                Assert.True(h.Service.Route(value));
                Assert.True(LargeFileHarness.Until(() => { lock (requeued) return requeued.Count == 1; }));
                Assert.Same(value, requeued.Single());
                Assert.Empty(dropped);

                // Known missing on this session: the next value is not taken; a new session tries the route again.
                Assert.False(h.Service.Route(FileValue(Piece + 1)));

                h.Transport.SessionNumber++;
                Assert.True(h.Service.Route(FileValue(Piece + 1)));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));
            }
        }

        [Fact]
        public void A_routed_value_goes_ahead_of_a_dump_in_progress_which_then_continues()
        {
            var (h, _, _) = Routing();

            using (h)
            {
                var path = h.WriteFile("big.dmp", Piece * 6);
                var content = File.ReadAllBytes(path);
                var routedOnce = 0;

                h.Transport.FakeChannel.BeforePieceAnswer = n =>
                {
                    if (n == 1 && Interlocked.Exchange(ref routedOnce, 1) == 0)
                        h.Service.Route(FileValue(Piece + 1));
                };

                var dump = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(h.AdvanceUntil(() => dump.IsCompleted));
                Assert.Equal(LargeFileOutcome.Stored, dump.Result.Outcome);
                Assert.Equal(content, h.Store.StoredFile("big.dmp"));
                Assert.Equal(1, h.Store.StoredCount("report.txt"));

                // big.dmp, then the routed value, then big.dmp again, continuing from the server's piece.
                Assert.Equal(new[] { "begin big.dmp", "begin report.txt", "begin big.dmp" }, h.Store.LogSnapshot().Where(l => l.StartsWith("begin")));
                Assert.Equal(6, h.Store.PieceNumbersSnapshot().Count(p => p >= 0) - 2);
            }
        }

        [Fact]
        public void Routed_values_waiting_at_stop_are_dropped_and_counted()
        {
            var (h, _, dropped) = Routing();

            using (h)
            {
                var hold = new ManualResetEventSlim(false);

                h.Transport.FakeChannel.HoldPieces = hold;
                Assert.True(h.Service.Route(FileValue(Piece * 3)));
                Assert.True(h.Service.Route(FileValue(Piece * 3)));
                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(Wait));

                h.Service.Stop();
                hold.Set();

                Assert.Equal(2, dropped.Sum());
                Assert.Equal(0, h.Service.RoutedCount);
                Assert.Contains(h.Errors, e => e.Contains("2 file value(s)"));
            }
        }

        [Fact]
        public void A_routed_value_waiting_longer_than_the_idle_time_keeps_the_uploader()
        {
            var (h, _, dropped) = Routing();

            using (h)
            {
                h.Timings.Idle = TimeSpan.FromSeconds(10);
                h.Store.AnswerBeginNext(FileAnswer.NoRoom(60));

                Assert.True(h.Service.Route(FileValue(Piece + 1)));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1, 30));
                Assert.Equal(2, h.Store.BeginCount("report.txt"));
                Assert.Empty(dropped);
            }
        }

        [Fact]
        public void Routed_values_waiting_are_bounded_in_bytes_and_one_is_always_taken()
        {
            var (h, _, dropped) = Routing();

            using (h)
            {
                h.Transport.Registration = _ => null;
                h.Service.MaxRoutedBytes = 10;

                // Larger than the bound, but alone: taken.
                Assert.True(h.Service.Route(FileValue(Piece + 1)));

                // Past the bound: not taken, the ordinary path has it (no drop here).
                Assert.False(h.Service.Route(FileValue(Piece + 2)));

                Assert.Equal(1, h.Service.RoutedCount);
                Assert.Empty(dropped);
                Assert.Single(h.Errors, e => e.Contains("the most this collector keeps"));
            }
        }

        [Fact]
        public void A_routed_value_whose_registration_is_not_acknowledged_in_60_s_goes_the_ordinary_way()
        {
            var (h, requeued, dropped) = Routing();

            using (h)
            {
                h.Transport.Registration = _ => null;

                var value = FileValue(Piece + 1);

                Assert.True(h.Service.Route(value));
                h.Steps(30);
                Assert.Empty(requeued);

                Assert.True(h.AdvanceUntil(() => { lock (requeued) return requeued.Count == 1; }));
                Assert.Same(value, requeued.Single());
                Assert.Empty(dropped);
            }
        }

        [Fact]
        public void The_comment_of_a_routed_value_is_cut_to_4096_characters()
        {
            var (h, _, _) = Routing();

            using (h)
            {
                Assert.True(h.Service.Route(FileValue(Piece + 1, SensorStatus.Ok, new string('c', 5000))));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));

                var comment = h.Store.Begins.Single().GetProperty("comment").GetString();

                Assert.Equal(4096, comment.Length);
                Assert.EndsWith("…", comment);
            }
        }

        [Fact]
        public void Any_409_to_end_begins_again_and_never_sends_a_piece_straight_away()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Store.AnswerEndNext(FileAnswer.Checking(1));
                h.Store.AnswerEndNext(FileAnswer.With(409, "{\"reason\":\"something new\"}"));
                h.Start();

                Assert.True(h.Service.Send("dumps", h.WriteFile("a.dmp", Piece * 2), null, CancellationToken.None).Wait(Wait));

                var log = h.Store.LogSnapshot();
                // begin, pieces, end (202), end (409), begin, end (stored): after the 409 the next request is a begin.
                Assert.Equal(new[] { "end", "end", "begin a.dmp", "end" }, log.Where(l => l == "end" || l.StartsWith("begin")).Skip(1));
                Assert.Equal(1, h.Store.StoredCount("a.dmp"));
            }
        }

        [Fact]
        public void Faults_of_the_end_check_are_bounded_then_the_file_waits_long()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                for (var i = 0; i < 10; i++)
                    h.Store.AnswerEndNext(FileAnswer.With(503, null, 1));

                h.Start();

                var task = h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None);

                Assert.True(h.AdvanceUntil(() => h.Store.LogSnapshot().Count(l => l == "end") == 10));
                h.Steps(5);
                Assert.False(task.IsCompleted);
                Assert.Single(h.Errors, e => e.Contains("could not check the large file") && e.Contains("30 minutes"));

                // After the long wait it is offered again, and stored.
                h.Timings.Advance(TimeSpan.FromMinutes(31));
                Assert.True(h.AdvanceUntil(() => task.IsCompleted));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
            }
        }

        [Fact]
        public void An_answer_that_asks_for_a_wait_without_retry_after_waits_30_s()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Store.AnswerEndNext(FileAnswer.With(202, "{\"state\":\"checking\"}"));
                h.Start();

                Assert.True(h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None).Wait(Wait));
                Assert.Contains(h.Timings.Waits, w => w.Kind == WaitKind.Retry && w.Span == TimeSpan.FromSeconds(30));
            }
        }

        [Fact]
        public void A_routed_value_whose_registration_was_answered_with_an_error_goes_the_ordinary_way()
        {
            var (h, requeued, dropped) = Routing();

            using (h)
            {
                h.Transport.Registration = _ => false;

                var value = FileValue(Piece + 1);

                Assert.True(h.Service.Route(value));
                Assert.True(LargeFileHarness.Until(() => { lock (requeued) return requeued.Count == 1; }));
                Assert.Same(value, requeued.Single());
                Assert.Empty(dropped);
                Assert.Equal(0, h.Store.BeginCount("report.txt"));
            }
        }

        [Fact]
        public void Answers_that_the_server_holds_no_such_upload_are_waited_for_and_bounded()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                for (var i = 0; i < 40; i++)
                    h.Store.AnswerPieceNext(FileAnswer.With(404));

                h.Start();

                var task = h.Service.Send("dumps", h.WriteFile("a.dmp", Piece * 2), null, CancellationToken.None);

                Assert.True(h.AdvanceUntil(() => task.IsCompleted, 60));
                Assert.Equal(LargeFileOutcome.GivenUp, task.Result.Outcome);
                Assert.True(h.Timings.Waits.Count(w => w.Kind == WaitKind.Retry) >= 9);
                Assert.InRange(h.Store.BeginCount("a.dmp"), 10, 40);
            }
        }

        [Fact]
        public void A_handed_over_file_that_cannot_be_read_is_missing()
        {
#if NETFRAMEWORK
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 10);
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                var security = File.GetAccessControl(path);
                var deny = new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.ReadData,
                                                                                  System.Security.AccessControl.AccessControlType.Deny);

                security.AddAccessRule(deny);
                File.SetAccessControl(path, security);

                try
                {
                    h.Start();

                    var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                    Assert.True(task.Wait(Wait));
                    Assert.Equal(LargeFileOutcome.Missing, task.Result.Outcome);
                    Assert.Equal(1, h.Dropped);
                }
                finally
                {
                    security.RemoveAccessRule(deny);
                    File.SetAccessControl(path, security);
                }
            }
#endif
        }

        [Fact]
        public void A_lock_that_cannot_be_opened_is_held_by_another_and_its_state_is_not_written()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("a.dmp", 10);
                Directory.CreateDirectory(h.DataFolder);

                var key = LargeFileStateStore.WatchKey("host/module/dumps", Path.GetFullPath(h.Folder), "*.dmp");
                var lockPath = Path.Combine(h.DataFolder, key + ".lock");

                File.WriteAllText(lockPath, string.Empty);
                File.SetAttributes(lockPath, FileAttributes.ReadOnly);

                try
                {
                    h.Start();

                    var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);

                    Assert.True(h.AdvanceUntil(() => watch.StateReason != null && watch.StateReason.Contains("another collector process")));
                    h.Steps(70);
                    Assert.Equal(0, h.Store.BeginCount("a.dmp"));
                    Assert.False(File.Exists(Path.Combine(h.DataFolder, key + ".json")));
                }
                finally
                {
                    File.SetAttributes(lockPath, FileAttributes.Normal);
                }
            }
        }

        [Fact]
        public void A_data_folder_that_cannot_be_written_makes_large_files_unavailable_said_once()
        {
            var (h, requeued, _) = Routing();

            using (h)
            {
                // A data folder under a file: it can never be made.
                var blocker = Path.Combine(h.Root, "blocker");

                File.WriteAllText(blocker, "x");

                var folder = Path.Combine(blocker, "data");

                h.StartWith((s, t) => new LargeFileService(t, null, new LargeFileStateStore(folder, h.Errors.Enqueue), LargeFileHarness.Registration, h.Errors.Enqueue,
                                                           n => true, null, h.Timings, n => { }, v => { lock (requeued) requeued.Add(v); }));

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);
                var again = h.Service.Watch("other", h.Folder, "*.dmp", null);
                var sent = h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None);

                Assert.Equal(LargeFileWatchState.Unavailable, watch.State);
                Assert.Contains(folder, watch.StateReason);
                Assert.Equal(LargeFileWatchState.Unavailable, again.State);
                Assert.True(sent.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Unavailable, sent.Result.Outcome);
                Assert.Single(h.Errors, e => e.Contains("cannot write its data folder"));

                // A routed value keeps no state in the data folder: it still goes on the file connection.
                var value = FileValue(Piece + 1);

                Assert.True(h.Service.Route(value));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));
                Assert.Equal(value.Value.ToArray(), h.Store.StoredFile("report.txt"));
                Assert.Empty(requeued);
                Assert.False(Directory.Exists(folder));
            }
        }

        [Fact]
        public void A_routed_value_after_a_start_goes_on_the_file_connection_whatever_the_data_folder()
        {
            var (h, requeued, _) = Routing();

            using (h)
            {
                var blocker = Path.Combine(h.Root, "blocker");

                File.WriteAllText(blocker, "x");

                var folder = Path.Combine(blocker, "data");

                h.StartWith((s, t) => new LargeFileService(t, null, new LargeFileStateStore(folder, h.Errors.Enqueue), LargeFileHarness.Registration, h.Errors.Enqueue,
                                                           n => true, null, h.Timings, n => { }, v => { lock (requeued) requeued.Add(v); }));

                // The probe found the folder unwritable; a Start takes it again (it is not probed before the next watch or
                // hand-over). Neither decides a routed value.
                Assert.Equal(LargeFileWatchState.Unavailable, h.Service.Watch("dumps", h.Folder, "*.dmp", null).State);
                h.Service.Stop();
                h.Service.Start();

                var first = FileValue(Piece + 1);

                Assert.True(h.Service.Route(first));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));

                // Probed again and still unwritable: the next routed value goes the same way.
                Assert.Equal(LargeFileWatchState.Unavailable, h.Service.Watch("other", h.Folder, "*.dmp", null).State);

                var second = FileValue(Piece + 2);

                Assert.True(h.Service.Route(second));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 2));
                Assert.Equal(second.Value.ToArray(), h.Store.StoredFile("report.txt"));
                Assert.Empty(requeued);
                Assert.False(Directory.Exists(folder));
            }
        }

        [Fact]
        public void A_data_folder_fixed_between_stop_and_start_is_noticed()
        {
            var (h, requeued, _) = Routing();

            using (h)
            {
                var blocker = Path.Combine(h.Root, "blocker");

                File.WriteAllText(blocker, "x");

                var folder = Path.Combine(blocker, "data");

                h.StartWith((s, t) => new LargeFileService(t, null, new LargeFileStateStore(folder, h.Errors.Enqueue), LargeFileHarness.Registration, h.Errors.Enqueue,
                                                           n => true, null, h.Timings, n => { }, v => { lock (requeued) requeued.Add(v); }));

                Assert.Equal(LargeFileWatchState.Unavailable, h.Service.Watch("dumps", h.Folder, "*.dmp", null).State);

                // The operator fixes it while the collector is stopped.
                h.Service.Stop();
                File.Delete(blocker);
                h.Service.Start();

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);

                Assert.Equal(LargeFileWatchState.Active, watch.State);
            }
        }

        [Fact]
        public void What_is_said_once_is_bounded()
        {
            var said = new SaidOnce();

            for (var i = 0; i < SaidOnce.Capacity * 3; i++)
                Assert.True(said.First("key " + i));

            Assert.Equal(SaidOnce.Capacity, said.Count);
            Assert.False(said.First("key " + (SaidOnce.Capacity * 3 - 1)));

            // The oldest were forgotten: said again.
            Assert.True(said.First("key 0"));
        }

        [Fact]
        public void A_comment_is_cut_on_a_character_boundary()
        {
            var comment = new string('c', LargeFileService.MaxBeginComment - 2) + "\U0001F600" + "tail";
            var cut = LargeFileService.CutComment(comment);

            Assert.True(cut.Length <= LargeFileService.MaxBeginComment);
            Assert.False(char.IsHighSurrogate(cut[cut.Length - 2]));
            Assert.EndsWith("\u2026", cut);
        }

        [Fact]
        public void A_1008_refuses_the_file_and_sends_nothing_more_until_the_next_start()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Store.AnswerPieceNext(FileAnswer.Close(1008));
                h.Start();

                var first = h.Service.Send("dumps", h.WriteFile("a.dmp", Piece * 2), null, CancellationToken.None);

                Assert.True(first.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Refused, first.Result.Outcome);

                var second = h.Service.Send("dumps", h.WriteFile("b.dmp", 10), null, CancellationToken.None);

                // Nothing goes on the file connection before the next Start: a routed value takes the ordinary path.
                Assert.False(h.Service.Route(FileValue(Piece + 1)));

                h.Steps(5);
                Assert.False(second.IsCompleted);
                Assert.Equal(0, h.Store.BeginCount("b.dmp"));
                Assert.Single(h.Errors, e => e.Contains("1008") && e.Contains("next start"));

                h.Service.Stop();
                h.Service.Start();

                Assert.True(h.AdvanceUntil(() => second.IsCompleted));
                Assert.Equal(LargeFileOutcome.Stored, second.Result.Outcome);
            }
        }

        [Fact]
        public void A_retry_after_is_waited_at_most_five_minutes()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Store.AnswerBeginNext(FileAnswer.Checking(3600));
                h.Start();

                Assert.True(h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None).Wait(Wait));
                Assert.Contains(h.Timings.Waits, w => w.Kind == WaitKind.Retry && w.Span == TimeSpan.FromMinutes(5));
                Assert.DoesNotContain(h.Timings.Waits, w => w.Span > TimeSpan.FromMinutes(5));
            }
        }

        [Fact]
        public void A_file_held_open_by_another_program_is_said_and_counted_once()
        {
            if (!SourceFile.IsWindows)
                return;

            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 10);

                h.Start();

                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                    Assert.True(h.AdvanceUntil(() => h.Dropped == 1));
                    h.Steps(20);
                    Assert.Equal(1, h.Dropped);
                    Assert.Single(h.Errors, e => e.Contains("held open by another program"));
                    Assert.False(task.IsCompleted);
                }

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 1));
            }
        }

        [Fact]
        public void The_record_of_a_file_that_went_is_discarded_on_the_server_at_the_next_run()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var gone = h.WriteFile("gone.dmp", Piece * 3);
                var hold = new ManualResetEventSlim(false);

                h.Start();

                var task = h.Service.Send("dumps", gone, null, CancellationToken.None);

                h.Transport.FakeChannel.BeforePieceAnswer = n => { if (n == 1) h.Transport.FakeChannel.HoldPieces = hold; };
                Assert.True(LargeFileHarness.Until(() => h.Store.PieceNumbersSnapshot().Count >= 2));
                h.Service.Dispose();
                hold.Set();
                h.Transport.FakeChannel.BeforePieceAnswer = null;
                h.Transport.FakeChannel.HoldPieces = null;

                Assert.Single(Directory.GetFiles(h.DataFolder, "upload-*.json"));
                File.Delete(gone);

                // The next run: the record's file is gone; the first upload of the run sweeps it.
                h.Start();
                Assert.True(h.Service.Send("dumps", h.WriteFile("next.dmp", 10), null, CancellationToken.None).Wait(Wait));

                Assert.Equal(1, h.Store.Discards);
                Assert.Empty(Directory.GetFiles(h.DataFolder, "upload-*.json"));
            }
        }

        [Fact]
        public void Routed_values_do_not_sweep_the_records_only_a_watch_or_a_hand_over_does()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var gone = h.WriteFile("gone.dmp", Piece * 3);
                var hold = new ManualResetEventSlim(false);

                h.Start();

                var task = h.Service.Send("dumps", gone, null, CancellationToken.None);

                h.Transport.FakeChannel.BeforePieceAnswer = n => { if (n == 1) h.Transport.FakeChannel.HoldPieces = hold; };
                Assert.True(LargeFileHarness.Until(() => h.Store.PieceNumbersSnapshot().Count >= 2));
                h.Service.Dispose();
                hold.Set();
                h.Transport.FakeChannel.BeforePieceAnswer = null;
                h.Transport.FakeChannel.HoldPieces = null;

                File.Delete(gone);

                // The next run only routes: the data folder is not used (as in the native collector), the record stays.
                var requeued = new List<FileSensorValue>();

                h.StartWith((s, t) => new LargeFileService(t, null, s, LargeFileHarness.Registration, h.Errors.Enqueue, n => true, null, h.Timings,
                                                           n => { }, v => { lock (requeued) requeued.Add(v); }));

                Assert.True(h.Service.Route(FileValue(Piece + 1)));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("report.txt") == 1));
                Assert.Equal(0, h.Store.Discards);
                Assert.Single(Directory.GetFiles(h.DataFolder, "upload-*.json"));

                // The first hand-over of the run sweeps it.
                Assert.True(h.Service.Send("dumps", h.WriteFile("next.dmp", 10), null, CancellationToken.None).Wait(Wait));

                Assert.Equal(1, h.Store.Discards);
                Assert.Empty(Directory.GetFiles(h.DataFolder, "upload-*.json"));
                Assert.Empty(requeued);
            }
        }

        [Fact]
        public void The_state_of_handed_over_files_does_not_keep_deleted_files()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();

                for (var i = 0; i < 3; i++)
                    Assert.True(h.Service.Send("dumps", h.WriteFile($"f{i}.dmp", 10), null, CancellationToken.None).Wait(Wait));

                var state = File.ReadAllText(Path.Combine(h.DataFolder, LargeFileStateStore.SentKey("host/module/dumps") + ".json"));

                Assert.DoesNotContain("f0.dmp", state);
                Assert.DoesNotContain("f2.dmp", state);
            }
        }

        [Fact]
        public void Handed_over_files_queued_at_dispose_are_said_and_counted()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();
                h.Service.Stop();

                h.Service.Send("dumps", h.WriteFile("a.dmp", 10), null, CancellationToken.None);
                h.Service.Send("dumps", h.WriteFile("b.dmp", 10), null, CancellationToken.None);
                h.Service.Dispose();

                Assert.Equal(2, h.Dropped);
                Assert.Contains(h.Errors, e => e.StartsWith("2 handed-over large file(s) were not sent"));
            }
        }
    }
}
