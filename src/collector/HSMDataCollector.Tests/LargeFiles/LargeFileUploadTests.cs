#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using Xunit;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>The upload of one file against the wire contract (#1888 design §3, §12): begin, pieces, end, every answer.</summary>
    public sealed class LargeFileUploadTests
    {
        private const int Piece = 65536;
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        private static LargeFileResult Send(LargeFileHarness h, string path, LargeFileOptions options = null)
        {
            var task = h.Service.Send("dumps", path, options, CancellationToken.None);

            Assert.True(task.Wait(Wait), "the hand-over did not settle");
            return task.Result;
        }

        [Fact]
        public void A_handed_file_goes_in_pieces_is_checked_and_its_source_deleted()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 3 + 1234);
                var content = File.ReadAllBytes(path);

                h.Start();
                var result = Send(h, path);

                Assert.Equal(LargeFileOutcome.Stored, result.Outcome);
                Assert.True(result.SourceDeleted);
                Assert.False(File.Exists(path));
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
                Assert.Equal(new long[] { 0, 1, 2, 3 }, h.Store.PieceNumbersSnapshot());

                var begin = h.Store.Begins.Single();
                Assert.Equal("host/module/dumps", begin.GetProperty("path").GetString());
                Assert.Equal(content.Length, begin.GetProperty("size").GetInt64());
                Assert.Equal("2026-11-25T10:00:00.0000000Z", begin.GetProperty("modified").GetString());
                Assert.Equal(Piece, begin.GetProperty("pieceBytes").GetInt32());

                if (SourceFile.IsWindows)
                    Assert.StartsWith("vol:", begin.GetProperty("source").GetString());
            }
        }

        [Fact]
        public void A_registration_asks_for_the_default_alert_unless_told_not_to()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();
                Send(h, h.WriteFile("a.dmp", 10));

                Assert.Equal(HSMSensorDataObjects.SensorRequests.DefaultAlertsOptions.LargeFiles, h.Transport.Registrations.Single().DefaultAlertsOptions);
                Assert.Null(h.Transport.Registrations.Single().Alerts);
            }

            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();
                Send(h, h.WriteFile("a.dmp", 10), new LargeFileOptions { NewFileAlert = false, SensorOptions = new FileSensorOptions { DefaultAlertsOptions = HSMSensorDataObjects.SensorRequests.DefaultAlertsOptions.DisableTtl } });

                Assert.Equal(HSMSensorDataObjects.SensorRequests.DefaultAlertsOptions.DisableTtl, h.Transport.Registrations.Single().DefaultAlertsOptions);
            }
        }

        [Fact]
        public void An_empty_file_is_one_begin_and_one_end()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("empty.dmp", 0);

                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.Empty(h.Store.PieceNumbersSnapshot());
                Assert.Equal(new[] { "open", "begin empty.dmp", "end" }, h.Store.LogSnapshot());
            }
        }

        [Fact]
        public void Keep_leaves_the_source_and_does_not_send_it_again_after_a_restart()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece + 5);
                var keep = new LargeFileOptions { DeleteAfterUpload = false };

                h.Start();
                var first = Send(h, path, keep);

                Assert.Equal(LargeFileOutcome.Stored, first.Outcome);
                Assert.False(first.SourceDeleted);
                Assert.True(File.Exists(path));

                h.Start();
                var second = Send(h, path, keep);

                Assert.Equal(LargeFileOutcome.Stored, second.Outcome);
                Assert.Equal(1, h.Store.BeginCount("a.dmp"));
            }
        }

        [Fact]
        public void An_upload_continues_from_the_servers_piece()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var content = File.ReadAllBytes(path);

                h.Store.Holds("a.dmp", content, 2);
                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.Equal(new long[] { 2, 3 }, h.Store.PieceNumbersSnapshot());
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_cut_connection_continues_from_the_servers_piece(bool pieceWasStored)
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var content = File.ReadAllBytes(path);

                h.Start();
                h.Store.AnswerPieceNext(FileAnswer.With(200, "{\"next\":1}"));
                h.Store.AnswerPieceNext(FileAnswer.Drop(pieceWasStored));

                // The first scripted answer took piece 0 without storing it: the server's real next after the cut is 0.
                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
                Assert.Equal(2, h.Transport.FakeChannel.Opens);
                Assert.Equal(2, h.Store.BeginCount("a.dmp"));
            }
        }

        [Theory]
        [InlineData("begin")]
        [InlineData("piece")]
        [InlineData("end")]
        public void A_connection_closed_by_the_server_at_any_point_continues(string at)
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 2 + 3);
                var content = File.ReadAllBytes(path);
                var close = FileAnswer.Close(1001);

                if (at == "begin") h.Store.AnswerBeginNext(close);
                if (at == "piece") h.Store.AnswerPieceNext(close);
                if (at == "end") h.Store.AnswerEndNext(close);

                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
            }
        }

        [Fact]
        public void A_lost_stored_answer_at_end_is_stored_at_the_next_begin_and_deleted()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece + 1);

                h.Store.AnswerEndNext(FileAnswer.Drop(applied: true));
                h.Start();

                var result = Send(h, path);

                Assert.Equal(LargeFileOutcome.Stored, result.Outcome);
                Assert.True(result.SourceDeleted);
                Assert.Equal(1, h.Store.StoredCount("a.dmp"));
            }
        }

        [Theory]
        [InlineData(413)]
        [InlineData(422)]
        [InlineData(400)]
        public void A_final_refusal_at_begin_leaves_the_file_and_is_counted_once(int status)
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                h.Store.AnswerBeginNext(FileAnswer.With(status));
                h.Start();

                var result = Send(h, path);

                Assert.Equal(LargeFileOutcome.Refused, result.Outcome);
                Assert.True(File.Exists(path));
                Assert.Equal(1, h.Dropped);
                Assert.Single(h.Errors, e => e.Contains("a.dmp") && e.Contains(status.ToString()));
            }
        }

        [Fact]
        public void A_file_over_the_servers_limit_is_refused_without_a_begin()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Store.MaxFileBytes = 1000;

                var path = h.WriteFile("big.dmp", 1001);

                h.Start();

                Assert.Equal(LargeFileOutcome.Refused, Send(h, path).Outcome);
                Assert.Equal(0, h.Store.BeginCount("big.dmp"));
                Assert.True(File.Exists(path));
            }
        }

        [Theory]
        [InlineData(503)]
        [InlineData(409)]
        public void A_refusal_with_retry_after_is_tried_again_later(int status)
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece + 1);

                h.Store.AnswerBeginNext(FileAnswer.With(status, null, 30));
                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(LargeFileHarness.Until(() => h.Store.BeginCount("a.dmp") == 1));
                Assert.False(task.Wait(300));

                Assert.True(h.AdvanceUntil(() => task.IsCompleted));
                Assert.True(h.Timings.Now >= TimeSpan.FromHours(1) + TimeSpan.FromSeconds(30), "the retry came before its retryAfter");
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Equal(2, h.Store.BeginCount("a.dmp"));
                Assert.Equal(0, h.Dropped);
            }
        }

        [Fact]
        public void Checking_is_asked_again_at_begin_and_at_end()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece + 1);

                h.Store.AnswerBeginNext(FileAnswer.Checking(5));
                h.Store.AnswerEndNext(FileAnswer.Checking(5));
                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.Equal(2, h.Store.BeginCount("a.dmp"));
                Assert.Equal(2, h.Store.LogSnapshot().Count(l => l == "end"));
                Assert.Equal(2, h.Timings.Waits.Count(w => w.Kind == WaitKind.Retry && w.Span == TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public void A_checksum_mismatch_sends_the_same_piece_again()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 2);

                h.Store.AnswerPieceNext(FileAnswer.With(200, "{\"next\":1}"));
                h.Store.AnswerPieceNext(FileAnswer.With(409, "{\"next\":0,\"reason\":\"checksum mismatch\"}"));
                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
            }
        }

        [Fact]
        public void An_end_mismatch_begins_again_from_zero_and_the_third_gives_the_file_up_for_good()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece + 1);

                for (var i = 0; i < 3; i++)
                    h.Store.AnswerEndNext(FileAnswer.Discarded());

                h.Start();

                var result = Send(h, path);

                Assert.Equal(LargeFileOutcome.GivenUp, result.Outcome);
                Assert.True(File.Exists(path));
                Assert.Equal(3, h.Store.BeginCount("a.dmp"));
                Assert.Equal(1, h.Dropped);

                // Persisted: after a restart the same file is not sent again, and it is reported as still there.
                h.Start();

                Assert.Equal(LargeFileOutcome.GivenUp, Send(h, path).Outcome);
                Assert.Equal(3, h.Store.BeginCount("a.dmp"));
            }
        }

        [Fact]
        public void A_file_that_changes_during_the_upload_is_discarded_and_sent_again()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 3);
                var changed = 0;

                // A change of the modification time after piece 1; on Windows no other process can write the open file, so
                // the change is made through the facts seam.
                h.Transport.FakeChannel.BeforePieceAnswer = n => { if (n == 1) Interlocked.Exchange(ref changed, 1); };
                h.Start();
                h.Timings.FactsHook = (p, facts) => Volatile.Read(ref changed) == 1
                    ? new FileFacts(facts.Name, facts.Size, facts.ModifiedUtc.AddHours(1), facts.Source)
                    : facts;

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(LargeFileHarness.Until(() => h.Store.Discards == 1), string.Join(" | ", h.Errors));

                h.Transport.FakeChannel.BeforePieceAnswer = null;

                // A changed hand-over is offered again after a scan interval.
                Assert.True(h.AdvanceUntil(() => task.IsCompleted), string.Join(" | ", h.Errors) + " || " + string.Join(",", h.Store.LogSnapshot()));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Equal("2026-11-25T11:00:00.0000000Z", h.Store.Begins.Last().GetProperty("modified").GetString());
                Assert.Equal(new[] { "discard" }, h.Store.LogSnapshot().Where(l => l == "discard"));
                Assert.Equal(0, h.Dropped);
            }
        }

        [Fact]
        public void On_windows_no_other_process_can_write_or_delete_a_file_while_it_is_sent()
        {
            if (!SourceFile.IsWindows)
                return;

            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 2);
                Exception write = null, delete = null;

                h.Transport.FakeChannel.BeforePieceAnswer = n =>
                {
                    if (n != 0)
                        return;

                    try { using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } }
                    catch (Exception ex) { write = ex; }

                    try { File.Delete(path); }
                    catch (Exception ex) { delete = ex; }
                };

                h.Start();

                Assert.Equal(LargeFileOutcome.Stored, Send(h, path).Outcome);
                Assert.IsAssignableFrom<IOException>(write);
                Assert.IsAssignableFrom<IOException>(delete);
                Assert.False(File.Exists(path));
            }
        }

        [Fact]
        public void A_handed_file_that_is_missing_is_missing_and_counted()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();

                var result = Send(h, Path.Combine(h.Folder, "none.dmp"));

                Assert.Equal(LargeFileOutcome.Missing, result.Outcome);
                Assert.Equal(1, h.Dropped);
            }
        }

        [Fact]
        public void The_speed_limit_waits_between_pieces_by_the_bytes_sent()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                // Random bytes do not compress: each frame is about a piece plus its header.
                var path = h.WriteFile("a.dmp", Piece * 4);

                h.Start();
                Assert.Equal(LargeFileOutcome.Stored, Send(h, path, new LargeFileOptions { MaxBytesPerSecond = Piece }).Outcome);

                var throttles = h.Timings.Waits.Where(w => w.Kind == WaitKind.Throttle).Select(w => w.Span).ToList();

                // None before the first piece; one before each of the three others, about a second each (a frame / the limit).
                Assert.Equal(3, throttles.Count);
                Assert.All(throttles, span => Assert.InRange(span.TotalSeconds, 0.99, 1.1));
            }
        }

        [Fact]
        public void No_limit_never_waits()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 3);

                h.Start();
                Assert.Equal(LargeFileOutcome.Stored, Send(h, path, new LargeFileOptions { MaxBytesPerSecond = 0 }).Outcome);
                Assert.DoesNotContain(h.Timings.Waits, w => w.Kind == WaitKind.Throttle);
            }
        }

        [Fact]
        public void A_server_without_large_files_is_said_once_and_the_file_waits()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 10);

                h.Transport.FakeChannel.RefuseOpens.Enqueue(404);
                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(LargeFileHarness.Until(() => h.Transport.FakeChannel.Opens == 1));
                Assert.False(task.Wait(300));
                Assert.Single(h.Errors, e => e.Contains("does not take large files"));

                // A new session is the first sign that an operator changed something.
                h.Transport.SessionNumber++;

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
            }
        }

        [Fact]
        public void A_refused_registration_refuses_the_file()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Transport.Answer = _ => new LargeFileRegistration(RegistrationOutcome.Refused, "not a File sensor");
                h.Start();

                var result = Send(h, h.WriteFile("a.dmp", 10));

                Assert.Equal(LargeFileOutcome.Refused, result.Outcome);
                Assert.Contains("not a File sensor", result.Reason);
                Assert.Equal(0, h.Store.BeginCount("a.dmp"));
            }
        }

        [Fact]
        public void A_collector_that_cannot_send_says_so_at_once()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start(unavailable: true);

                var result = Send(h, h.WriteFile("a.dmp", 10));
                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);

                Assert.Equal(LargeFileOutcome.Unavailable, result.Outcome);
                Assert.Equal(LargeFileWatchState.Unavailable, watch.State);
                Assert.Contains("HTTP", watch.StateReason);
                Assert.Single(h.Errors);
            }
        }
    }
}
