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
    /// <summary>Watched folders (#1888 design §2, §12): the settle rule, files found at start, the order, the state file.</summary>
    public sealed class LargeFileWatchTests
    {
        private const int Piece = 65536;
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(60);

        private static LargeFileOptions Options(bool delete = true) => new LargeFileOptions { SettleTime = Settle, DeleteAfterUpload = delete };

        // Gives the uploader's thread a scan that sees the folder as it is now, then advances the test's clock past the settle
        // time and gives it the scans that take the settled files.
        // The clock moves a second at a time, so every scan sees the folder as it is: what has not changed for the settle time
        // by the end is taken, whenever the uploader's thread gets to it.
        private static void Settled(LargeFileHarness h) => h.Steps((int)Settle.TotalSeconds + 10);

        [Fact]
        public void A_growing_file_is_taken_only_when_it_stopped_changing_for_the_settle_time()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 1000);

                h.Start();
                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());

                // It grows: every change starts its settle time again.
                for (var i = 1; i <= 3; i++)
                {
                    h.Steps(20);

                    File.WriteAllBytes(path, SeededContent.Make(1000 + i * 100, 7));
                    File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 25, 10, i, 0, DateTimeKind.Utc));
                }

                h.Steps(2);
                Assert.Equal(0, h.Store.BeginCount("a.dmp"));

                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 1));
                Assert.Equal(1300, h.Store.StoredFile("a.dmp").Length);
                Assert.True(h.AdvanceUntil(() => !File.Exists(path)));
                Assert.Equal(1, watch.FilesStored);
            }
        }

        [Fact]
        public void Files_found_at_start_are_taken_oldest_first_and_only_those_that_match()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("c.dmp", 10, modified: new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
                h.WriteFile("a.dmp", 10, modified: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
                h.WriteFile("b.dmp", 10, modified: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
                h.WriteFile("a.dmp_old", 10);
                h.WriteFile("notes.txt", 10);

                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("c.dmp") == 1));
                Assert.Equal(new[] { "begin a.dmp", "begin b.dmp", "begin c.dmp" }, h.Store.LogSnapshot().Where(l => l.StartsWith("begin")));
                Assert.True(File.Exists(Path.Combine(h.Folder, "a.dmp_old")));
                Assert.True(File.Exists(Path.Combine(h.Folder, "notes.txt")));
            }
        }

        [Fact]
        public void A_watch_made_before_start_polls_after_start()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("a.dmp", 10);
                h.Start();
                h.Service.Stop();

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());

                Assert.Equal(LargeFileWatchState.Active, watch.State);
                Settled(h);
                h.Steps(5);
                Assert.Equal(0, h.Store.BeginCount("a.dmp"));

                h.Service.Start();
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 1));
            }
        }

        [Fact]
        public void Kept_files_are_not_sent_again_after_a_restart_but_a_new_file_under_the_old_name_is()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options(delete: false));
                Settled(h);
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 1));

                // A restart of the collector: a new service on the same data folder.
                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options(delete: false));
                Settled(h);
                h.Steps(5);
                Assert.Equal(1, h.Store.BeginCount("a.dmp"));

                // A new dump under the same name.
                File.WriteAllBytes(path, SeededContent.Make(200, 9));
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc));
                Settled(h);
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.BeginCount("a.dmp") == 2));
                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 2));
            }
        }

        [Fact]
        public void A_delivered_file_left_by_a_crash_before_its_delete_is_deleted_at_the_next_start()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                h.Start();

                // Keep first (the state says delivered), then a watch that deletes: what a crash between the two leaves.
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options(delete: false)).Dispose();
                var keep = h.Service.Send("dumps", path, new LargeFileOptions { DeleteAfterUpload = false }, CancellationToken.None);
                Assert.True(keep.Wait(20000));
                h.Service.Dispose();

                var key = LargeFileStateStore.WatchKey("host/module/dumps", Path.GetFullPath(h.Folder), "*.dmp");
                var sent = LargeFileStateStore.SentKey("host/module/dumps");
                File.Copy(Path.Combine(h.DataFolder, sent + ".json"), Path.Combine(h.DataFolder, key + ".json"), true);

                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => !File.Exists(path)));

                // The state's word alone deletes nothing: a begin with the file's facts answered `stored` first.
                Assert.Equal(2, h.Store.BeginCount("a.dmp"));
                Assert.Equal(1, h.Store.StoredCount("a.dmp"));
            }
        }

        [Fact]
        public void A_delivered_entry_the_server_does_not_confirm_is_uploaded_not_deleted()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                h.Start();

                var keep = h.Service.Send("dumps", path, new LargeFileOptions { DeleteAfterUpload = false }, CancellationToken.None);
                Assert.True(keep.Wait(20000));
                h.Service.Dispose();

                var key = LargeFileStateStore.WatchKey("host/module/dumps", Path.GetFullPath(h.Folder), "*.dmp");
                var sent = LargeFileStateStore.SentKey("host/module/dumps");
                File.Copy(Path.Combine(h.DataFolder, sent + ".json"), Path.Combine(h.DataFolder, key + ".json"), true);

                // A server that no longer has it (another server, retention): its begin answers "send from piece 0".
                h.Store.AnswerBeginNext(FileAnswer.With(200, "{\"upload\":\"000000000000000000000000000000aa\",\"next\":0,\"pieces\":1,\"pieceBytes\":65536}"));
                h.Store.AnswerPieceNext(FileAnswer.Drop());

                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.BeginCount("a.dmp") >= 3));
                Assert.True(h.AdvanceUntil(() => !File.Exists(path)));
                Assert.Equal(1, h.Store.StoredCount("a.dmp"));
            }
        }

        [Fact]
        public void A_given_up_file_is_reported_again_at_every_start_and_not_sent()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", 100);

                for (var i = 0; i < 3; i++)
                    h.Store.AnswerEndNext(FileAnswer.Discarded());

                h.Start();
                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => watch.FilesNotSent == 1));
                Assert.True(File.Exists(path));

                h.Start();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Errors.Count(e => e.Contains("still on the disk unsent")) == 1));
                h.Steps(5);
                Assert.Equal(3, h.Store.BeginCount("a.dmp"));
            }
        }

        [Fact]
        public void A_refusal_is_not_tried_again_before_the_next_start()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("a.dmp", 100);
                h.Store.AnswerBeginNext(FileAnswer.With(422));

                h.Start();
                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => watch.FilesNotSent == 1));
                Settled(h);
                h.Steps(5);
                Assert.Equal(1, h.Store.BeginCount("a.dmp"));

                h.Service.Stop();
                h.Service.Start();
                Settled(h);

                Assert.True(h.AdvanceUntil(() => h.Store.StoredCount("a.dmp") == 1));
            }
        }

        [Fact]
        public void A_file_matched_by_two_watches_is_taken_once_by_the_first()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.WriteFile("a.dmp", 100);
                h.Start();

                var first = h.Service.Watch("first", h.Folder, "*.dmp", Options());
                var second = h.Service.Watch("second", h.Folder, "a.*", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => first.FilesStored == 1));
                h.Steps(5);
                Assert.Equal(1, h.Store.BeginCount("a.dmp"));
                Assert.Equal(0, second.FilesStored);
                Assert.Equal("host/module/first", h.Store.Begins.Single().GetProperty("path").GetString());
            }
        }

        [Fact]
        public void A_missing_folder_is_said_once_and_its_files_are_taken_when_it_comes()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var folder = Path.Combine(h.Root, "later");

                h.Start();
                var watch = h.Service.Watch("dumps", folder, "*.dmp", Options());

                Assert.True(h.AdvanceUntil(() => watch.StateReason != null));
                Assert.Equal(LargeFileWatchState.Active, watch.State);

                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "a.dmp"), new byte[] { 1, 2, 3 });
                Settled(h);

                Assert.True(h.AdvanceUntil(() => watch.FilesStored == 1));
                Assert.Single(h.Errors, e => e.Contains("is not there"));
            }
        }

        [Fact]
        public void Disposing_a_watch_cuts_its_upload_and_the_part_continues_later()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                var path = h.WriteFile("a.dmp", Piece * 3);
                var hold = new ManualResetEventSlim(false);

                h.Transport.FakeChannel.HoldPieces = hold;
                h.Start();

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.Transport.FakeChannel.PieceArrived.Wait(15000));
                watch.Dispose();

                Assert.Equal(LargeFileWatchState.Disposed, watch.State);
                Assert.True(File.Exists(path));

                h.Transport.FakeChannel.HoldPieces = null;
                hold.Set();

                var again = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
                Settled(h);

                Assert.True(h.AdvanceUntil(() => again.FilesStored == 1));
            }
        }

        [Fact]
        public void An_equal_live_watch_is_a_mistake_of_the_call_and_one_after_dispose_is_not()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();

                var watch = h.Service.Watch("dumps", h.Folder, "*.dmp", Options());

                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", h.Folder, "*.dmp", Options()));

                watch.Dispose();
                h.Service.Watch("dumps", h.Folder, "*.dmp", Options());
            }
        }

        [Fact]
        public void Mistakes_of_the_call_throw()
        {
            using (var h = new LargeFileHarness(Piece))
            {
                h.Start();

                Assert.Throws<ArgumentException>(() => h.Service.Watch("", h.Folder, "*.dmp", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("a\\b", h.Folder, "*.dmp", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", "", "*.dmp", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", h.Folder, "", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", h.Folder, "x/*.dmp", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", h.Folder, "[ab].dmp", null));
                Assert.Throws<ArgumentException>(() => h.Service.Watch("dumps", h.Folder, "a;b", null));
                Assert.Throws<ArgumentOutOfRangeException>(() => h.Service.Watch("dumps", h.Folder, "*.dmp", new LargeFileOptions { SettleTime = TimeSpan.FromMilliseconds(500) }));
                Assert.Throws<ArgumentOutOfRangeException>(() => h.Service.Watch("dumps", h.Folder, "*.dmp", new LargeFileOptions { MaxBytesPerSecond = -1 }));
                Exception thrown = null;

                try { h.Service.Send("dumps", "", null, CancellationToken.None); }
                catch (Exception ex) { thrown = ex; }

                Assert.IsType<ArgumentException>(thrown);
            }
        }
    }
}
