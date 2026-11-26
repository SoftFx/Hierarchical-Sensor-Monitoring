#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.LargeFiles;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using Xunit;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>Stop, Dispose, restarts, the state file, bounds and isolation (#1888 design §12.6–§12.8, §12.12).</summary>
    public sealed class LargeFileLifecycleTests
    {
        private const int Piece = 65536;
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        [Fact]
        public void Stop_in_the_middle_does_not_wait_and_the_upload_continues_after_start()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var content = File.ReadAllBytes(path);
                var hold = new ManualResetEventSlim(false);

                h.Store.AnswerPieceNext(FileAnswer.With(200, "{\"next\":1}"));

                // From the answer of piece 1 on, pieces are held: the next one to arrive (piece 1 again, the server's next
                // being 1) is the one Stop cuts, whatever the speed of the machine.
                h.Transport.FakeChannel.BeforePieceAnswer = n =>
                {
                    if (n != 1 || h.Transport.FakeChannel.HoldPieces != null)
                        return;

                    h.Transport.FakeChannel.PieceArrived.Reset();
                    h.Transport.FakeChannel.HoldPieces = hold;
                };

                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(Wait));
                Assert.True(LargeFileHarness.Until(() => h.Transport.FakeChannel.HoldPieces != null));
                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(Wait));

                var stopping = Stopwatch.StartNew();
                h.Service.Stop();

                Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(3), $"Stop took {stopping.Elapsed}");
                Assert.False(task.IsCompleted);
                Assert.True(File.Exists(path));

                h.Transport.FakeChannel.BeforePieceAnswer = null;
                h.Transport.FakeChannel.HoldPieces = null;
                hold.Set();
                h.Service.Start();

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
            }
        }

        [Fact]
        public void Dispose_in_the_middle_cancels_the_hand_over_and_does_not_wait()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var hold = new ManualResetEventSlim(false);

                h.Transport.FakeChannel.HoldPieces = hold;
                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(Wait));

                var disposing = Stopwatch.StartNew();
                h.Service.Dispose();

                Assert.True(disposing.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {disposing.Elapsed}");
                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Cancelled, task.Result.Outcome);
                Assert.True(File.Exists(path));

                // After Dispose: nothing throws, the answers say so.
                Assert.Equal(LargeFileWatchState.Disposed, h.Service.Watch("dumps", h.Folder, "*.dmp", null).State);
                Assert.Equal(LargeFileOutcome.Cancelled, h.Service.Send("dumps", path, null, CancellationToken.None).Result.Outcome);
                h.Service.Dispose();
                h.Service.Stop();
                h.Service.Start();
                hold.Set();
            }
        }

        [Fact]
        public void A_cancelled_token_cancels_the_hand_over()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var hold = new ManualResetEventSlim(false);
                var cancel = new CancellationTokenSource();

                h.Transport.FakeChannel.HoldPieces = hold;
                h.Start();

                var task = h.Service.Send("dumps", path, null, cancel.Token);

                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(Wait));
                cancel.Cancel();

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Cancelled, task.Result.Outcome);
                hold.Set();
            }
        }

        [Fact]
        public void The_same_file_handed_over_twice_while_pending_gets_the_same_task()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece);

                h.Service?.Dispose();
                h.Start();
                h.Service.Stop();

                var first = h.Service.Send("dumps", path, null, CancellationToken.None);
                var second = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.Same(first, second);

                h.Service.Start();
                Assert.True(first.Wait(Wait));
                Assert.Equal(1, h.Store.BeginCount("a.dmp"));
            }
        }

        [Fact]
        public void The_thread_exists_only_while_there_is_work()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();
                Assert.False(h.Service.ThreadAlive);

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);
                Assert.True(h.Service.ThreadAlive);

                watch.Dispose();

                // The idle time counts from when the thread saw no work; the clock moves an hour per step until it ends.
                Assert.True(LargeFileHarness.Until(() =>
                {
                    h.Timings.Advance(TimeSpan.FromHours(1));
                    h.Service.Wake();
                    Thread.Sleep(10);
                    return !h.Service.ThreadAlive;
                }));
            }
        }

        [Fact]
        public void Public_calls_from_many_threads_while_starting_and_stopping_never_throw()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                for (var i = 0; i < 8; i++)
                    h.WriteFile($"f{i}.dmp", 1000 + i, (uint)i + 1);

                h.Start();

                var failures = new List<Exception>();
                var stop = DateTime.UtcNow.AddSeconds(3);
                var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
                {
                    var n = 0;

                    while (DateTime.UtcNow < stop)
                    {
                        try
                        {
                            switch ((t + n++) % 5)
                            {
                                case 0: h.Service.Watch($"w{t}", h.Folder, $"f{t}.*", null).Dispose(); break;
                                case 1: h.Service.Send("dumps", Path.Combine(h.Folder, $"f{t}.dmp"), new LargeFileOptions { DeleteAfterUpload = false }, CancellationToken.None); break;
                                case 2: h.Service.Stop(); break;
                                case 3: h.Service.Start(); break;
                                default:
                                    var w = h.Service.Watch($"x{t}-{n}", h.Folder, "*.dmp", null);
                                    _ = w.State; _ = w.FilesStored; _ = w.FilesNotSent; _ = w.StateReason;
                                    w.Dispose();
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (failures)
                                failures.Add(ex);
                        }
                    }
                })).ToList();

                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());

                Assert.Empty(failures);

                // The service still works afterwards.
                h.Service.Start();
                var result = h.Service.Send("dumps", h.WriteFile("last.dmp", 77), null, CancellationToken.None);
                Assert.True(result.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, result.Result.Outcome);
            }
        }

        [Fact]
        public void A_fault_of_the_transport_is_reported_and_the_file_is_tried_again()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece);
                var calls = 0;

                h.Transport.Answer = _ =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                        throw new InvalidOperationException("boom");

                    return new LargeFileRegistration(RegistrationOutcome.Accepted, null);
                };
                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(LargeFileHarness.Until(() => h.Errors.Any(e => e.Contains("boom"))));
                Assert.True(h.AdvanceUntil(() => task.IsCompleted));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
            }
        }

        [Fact]
        public void Three_protocol_closes_give_the_file_up()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 2);

                for (var i = 0; i < 3; i++)
                    h.Store.AnswerPieceNext(FileAnswer.Close(1002));

                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.GivenUp, task.Result.Outcome);
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void Ten_checksum_mismatches_give_the_file_up_and_three_in_a_row_reconnect()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 2);

                for (var i = 0; i < 10; i++)
                    h.Store.AnswerPieceNext(FileAnswer.With(409, "{\"next\":0,\"reason\":\"checksum mismatch\"}"));

                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.GivenUp, task.Result.Outcome);
                Assert.Equal(4, h.Transport.FakeChannel.Opens);
            }
        }

        [Fact]
        public void An_out_of_order_answer_that_goes_back_and_a_repeat_that_jumps_ahead_keep_the_digests_whole()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var content = File.ReadAllBytes(path);

                // Piece 0 is stored; piece 1 is answered "out of order, go on from 0" (back); piece 0 again is a repeat the server
                // answers with its next (1, ahead of what was sent). The digest of each piece is kept by its number.
                h.Store.AnswerPieceNext(null);
                h.Store.AnswerPieceNext(FileAnswer.With(409, "{\"next\":0,\"reason\":\"out of order\"}"));
                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Equal(new long[] { 0, 1, 0, 1, 2, 3 }, h.Store.PieceNumbersSnapshot());
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
            }
        }

        [Fact]
        public void After_a_kill_the_digests_of_sent_pieces_come_from_the_data_folder()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 4);
                var content = File.ReadAllBytes(path);
                var hold = new ManualResetEventSlim(false);

                h.Start();

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                // Two pieces stored, then the process "dies" while the third waits.
                h.Transport.FakeChannel.BeforePieceAnswer = n => { if (n == 2) h.Transport.FakeChannel.HoldPieces = hold; };
                Assert.True(LargeFileHarness.Until(() => h.Store.PieceNumbersSnapshot().Count >= 2));
                h.Service.Dispose();
                hold.Set();
                h.Transport.FakeChannel.BeforePieceAnswer = null;
                h.Transport.FakeChannel.HoldPieces = null;

                var digests = Directory.GetFiles(h.DataFolder, "upload-*.digests");
                Assert.Single(digests);

                h.Start();
                var again = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(again.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, again.Result.Outcome);
                Assert.Equal(content, h.Store.StoredFile("a.dmp"));
                Assert.Empty(Directory.GetFiles(h.DataFolder, "upload-*.json"));
                Assert.Empty(Directory.GetFiles(h.DataFolder, "upload-*.digests"));
            }
        }

        [Fact]
        public void A_corrupt_state_file_is_put_aside_and_said()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                Directory.CreateDirectory(h.DataFolder);
                File.WriteAllText(Path.Combine(h.DataFolder, LargeFileStateStore.SentKey("host/module/dumps") + ".json"), "{ not json");

                h.Start();
                var task = h.Service.Send("dumps", path, new LargeFileOptions { DeleteAfterUpload = false }, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.Single(Directory.GetFiles(h.DataFolder, "*.corrupt-*"));
                Assert.Contains(h.Errors, e => e.Contains("not readable"));
            }
        }

        [Fact]
        public void Without_an_identity_a_stored_file_at_begin_is_kept_unless_this_collector_sent_it()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                h.Store.AnswerBeginNext(FileAnswer.Stored());
                h.Start();
                h.Timings.FactsHook = (p, facts) => new FileFacts(facts.Name, facts.Size, facts.ModifiedUtc, null);

                var task = h.Service.Send("dumps", path, null, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Equal(LargeFileOutcome.Stored, task.Result.Outcome);
                Assert.False(task.Result.SourceDeleted);
                Assert.True(File.Exists(path));
                Assert.Contains(h.Errors, e => e.Contains("no record of having sent"));
            }
        }

        [Fact]
        public void A_slow_link_is_said_once()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Transport.FakeChannel.LastPieceWrite = TimeSpan.FromSeconds(120);
                h.Start();

                var task = h.Service.Send("dumps", h.WriteFile("a.dmp", Piece * 3), null, CancellationToken.None);

                Assert.True(task.Wait(Wait));
                Assert.Single(h.Errors, e => e.Contains("The link to the server passes about"));
            }
        }

        [Fact]
        public void Two_services_on_one_data_folder_do_not_watch_the_same_folder_at_once()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("a.dmp", 100);

                var other = new LargeFileService(h.Transport, null, new LargeFileStateStore(h.DataFolder, _ => { }), LargeFileHarness.Registration,
                                                 _ => { }, _ => true, null, new VirtualTimings());

                try
                {
                    other.Start();
                    var held = other.Watch("dumps", h.Folder, "*.dmp", null);

                    Assert.True(LargeFileHarness.Until(() => Directory.Exists(h.DataFolder) && Directory.GetFiles(h.DataFolder, "watch-*.lock").Length == 1));

                    h.Start();
                    var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", null);

                    Assert.True(LargeFileHarness.Until(() => watch.StateReason != null && watch.StateReason.Contains("another collector process")));
                    held.Dispose();
                }
                finally
                {
                    other.Dispose();
                }
            }
        }
    }
}
