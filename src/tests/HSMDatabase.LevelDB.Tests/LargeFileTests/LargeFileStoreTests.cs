using System.Security.Cryptography;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.LargeFileTests
{
    /// <summary>
    /// The large file store (#1883): the layout, the order of an append, leases, the limits, the free-space reservation,
    /// the check at open, the store's own pass, and the archive calls of step 3 (#1885).
    /// </summary>
    public sealed class LargeFileStoreTests : IDisposable
    {
        private const int Piece = LargeFileStore.MinPieceSize;
        private static readonly DateTime T0 = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-large-files-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _sensor = Guid.NewGuid();
        private readonly List<LargeFileStore> _stores = [];

        private DateTime _now = T0;
        // When set, the monotonic clock of leases stops following the wall clock (a step of the wall clock).
        private long? _monotonicMs;
        private long _free = long.MaxValue / 2;


        public void Dispose()
        {
            foreach (var store in _stores)
                store.Dispose();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        // === Layout, append, finish, read back ===

        [Fact]
        public void Nothing_is_created_before_the_first_begin_and_a_refused_begin_creates_nothing()
        {
            var store = Open(new LargeFileStoreLimits { MaxFileSizeBytes = 10 * Piece });

            store.EnsureOpened();
            Assert.True(store.IsOpened);
            Assert.Empty(store.GetFiles());
            Assert.Equal(LargeFileStorePassReport.Empty, store.RunStorePass());

            Assert.Equal(LargeFileBeginOutcome.TooLarge, Begin(store, Key("a", 11 * Piece)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("a", Piece), null, Piece - 1, Piece).Outcome);
            _free = 0;
            Assert.Equal(LargeFileBeginOutcome.NoRoom, Begin(store, Key("a", Piece)).Outcome);

            Assert.False(store.Exists);
            Assert.False(Directory.Exists(_root));
        }

        [Fact]
        public void An_upload_is_appended_finished_and_read_back_and_its_files_are_named_by_ids()
        {
            var store = Open();
            var content = Content(3 * Piece + 100);
            var begin = Begin(store, Key("app.dmp", content.Length));

            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);
            Assert.Equal(0, begin.NextPiece);
            Assert.Equal(4, begin.PieceCount);
            Assert.Equal(Piece, begin.PieceSize);
            Assert.Equal(begin.FileId, begin.Lease.FileId);

            var uploads = Path.Combine(_root, "uploads", _product.ToString("N"));
            Assert.True(File.Exists(Path.Combine(uploads, $"{begin.FileId:N}.part")));
            Assert.True(File.Exists(Path.Combine(uploads, $"{begin.FileId:N}.idx")));
            Assert.True(File.Exists(Path.Combine(uploads, $"{begin.FileId:N}.rec")));
            Assert.False(Directory.Exists(Path.Combine(_root, "archives")));

            AppendAll(store, begin.Lease, content);

            var record = store.TryGetFile(begin.FileId);
            Assert.Equal(LargeFileState.Uploading, record.State);
            Assert.Equal(4, record.PieceCount);
            Assert.Equal(content.Length, record.PartLength);
            Assert.Equal(content.Length, new FileInfo(Path.Combine(uploads, $"{begin.FileId:N}.part")).Length);
            Assert.Equal(4 * LargeFileStore.IndexEntrySize, new FileInfo(Path.Combine(uploads, $"{begin.FileId:N}.idx")).Length);
            Assert.Equal(4, store.Statistics.PiecesStored);

            var ticket = store.StartFinish(begin.Lease);
            Assert.Equal(LargeFileFinishStart.Ready, ticket.Outcome);

            var pieces = ReadPieces(ticket.Reader);
            Assert.Equal(content, pieces.SelectMany(p => p.Bytes.ToArray()).ToArray());
            Assert.Equal(Digest(content.AsSpan(Piece, Piece)), pieces[1].Digest.ToArray());
            Assert.Equal([0L, 1, 2, 3], pieces.Select(p => p.Number));

            var hash = SHA256.HashData(content);
            var verified = store.MarkVerified(begin.FileId, hash, T0.AddSeconds(1));
            store.EndFinish(begin.Lease);

            Assert.Equal(LargeFileState.Verified, verified.State);
            Assert.Equal(hash, verified.Hash);
            Assert.Equal(T0.AddSeconds(1), verified.ValueTimeUtc);

            using (var part = store.OpenPart(begin.FileId))
            using (var copy = new MemoryStream())
            {
                part.CopyTo(copy);
                Assert.Equal(content, copy.ToArray());
            }

            // The record is the state on the disk: a restart reads it back as it was.
            store.Dispose();
            var reopened = Open();
            var back = reopened.TryGetFile(begin.FileId);

            Assert.Equal(LargeFileState.Verified, back.State);
            Assert.Equal(hash, back.Hash);
            Assert.Equal("app.dmp", back.Name);
            Assert.Equal("dmp", back.Extension);
            Assert.Equal(T0.AddSeconds(1), back.ValueTimeUtc);
            Assert.Equal(_sensor, back.SensorId);
            Assert.Equal(T0.AddDays(-1), back.SourceModifiedUtc);
            Assert.Equal(DateTimeKind.Utc, back.SourceModifiedUtc.Kind);
            Assert.True(reopened.HasFiles(_sensor));
            Assert.False(reopened.HasFiles(Guid.NewGuid()));
            Assert.Equal(ReadPieces(reopened.OpenPieces(begin.FileId)).SelectMany(p => p.Bytes.ToArray()), content);

            var stored = Begin(reopened, Key("app.dmp", content.Length));
            Assert.Equal(LargeFileBeginOutcome.AlreadyStored, stored.Outcome);
            Assert.Equal(Piece, stored.PieceSize);
        }

        [Fact]
        public void A_file_of_zero_bytes_has_no_piece_and_is_ready_to_finish_at_once()
        {
            var store = Open();
            var begin = Begin(store, Key("empty", 0));

            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);
            Assert.Equal(0, begin.PieceCount);
            Assert.Equal(LargeFileAppendOutcome.TooMany, Append(store, begin.Lease, 0, [1]).Outcome);
            Assert.Equal(LargeFileFinishStart.Ready, store.StartFinish(begin.Lease).Outcome);
        }

        [Fact]
        public void Appends_out_of_order_twice_or_beyond_the_last_piece_are_answered_without_a_write()
        {
            var store = Open();
            var begin = Begin(store, Key("a", 2 * Piece));

            Assert.Equal(new LargeFileAppendResult(LargeFileAppendOutcome.OutOfOrder, 0), Append(store, begin.Lease, 1, Content(Piece)));
            Assert.Equal(new LargeFileAppendResult(LargeFileAppendOutcome.Stored, 1), Append(store, begin.Lease, 0, Content(Piece)));
            Assert.Equal(new LargeFileAppendResult(LargeFileAppendOutcome.AlreadyHave, 1), Append(store, begin.Lease, 0, Content(Piece)));
            Assert.Equal(new LargeFileAppendResult(LargeFileAppendOutcome.TooMany, 1), Append(store, begin.Lease, 2, Content(Piece)));
            Assert.Equal(Piece, store.TryGetFile(begin.FileId).PartLength);

            Assert.Equal(LargeFileFinishStart.Incomplete, store.StartFinish(begin.Lease).Outcome);
            Assert.Equal(1, store.StartFinish(begin.Lease).NextPiece);
        }

        [Fact]
        public void A_stored_length_or_digest_outside_the_bounds_is_the_callers_error()
        {
            var store = Open();
            var begin = Begin(store, Key("a", Piece));

            Assert.Throws<ArgumentException>(() => store.Append(begin.Lease, 0, [], Digest([1])));
            Assert.Throws<ArgumentException>(() => store.Append(begin.Lease, 0, new byte[LargeFileStore.MaxStoredPieceBytes(Piece) + 1], Digest([1])));
            Assert.Throws<ArgumentException>(() => store.Append(begin.Lease, 0, [1, 2], new byte[31]));

            // The zstd bound of the maximum piece size is accepted: an incompressible piece grows a little.
            Assert.Equal(LargeFileAppendOutcome.Stored, Append(store, begin.Lease, 0, new byte[LargeFileStore.MaxStoredPieceBytes(Piece)]).Outcome);
        }

        // === Two uploads of one file ===

        [Fact]
        public void A_begin_of_the_same_file_continues_it_after_a_restart_with_the_same_file_id()
        {
            var store = Open();
            var content = Content(3 * Piece);
            var begin = Begin(store, Key("a", content.Length));
            Append(store, begin.Lease, 0, content.AsSpan(0, Piece).ToArray());

            store.Dispose();
            var reopened = Open();
            var again = Begin(reopened, Key("a", content.Length));

            Assert.Equal(LargeFileBeginOutcome.Continue, again.Outcome);
            Assert.Equal(begin.FileId, again.FileId);
            Assert.Equal(1, again.NextPiece);
            Assert.Equal(Piece, again.PieceSize);
            Assert.NotEqual(begin.Lease.LeaseId, again.Lease.LeaseId);

            AppendAll(reopened, again.Lease, content, from: 1);
            Assert.Equal(content, ReadPieces(reopened.StartFinish(again.Lease).Reader).SelectMany(p => p.Bytes.ToArray()));
        }

        [Fact]
        public void A_live_lease_makes_another_begin_busy_until_it_expires_or_is_released()
        {
            var store = Open();
            var key = Key("a", 2 * Piece);
            var first = Begin(store, key);

            var busy = Begin(store, key);
            Assert.Equal(LargeFileBeginOutcome.Busy, busy.Outcome);
            Assert.Equal(TimeSpan.FromSeconds(180), busy.RetryAfter);

            // An append renews the lease.
            _now = _now.AddSeconds(100);
            Append(store, first.Lease, 0, Content(Piece));
            _now = _now.AddSeconds(100);
            Assert.Equal(LargeFileBeginOutcome.Busy, Begin(store, key).Outcome);

            // Expired: the next begin takes the part over, and the old lease is lost.
            _now = _now.AddSeconds(81);
            var second = Begin(store, key);
            Assert.Equal(LargeFileBeginOutcome.Continue, second.Outcome);
            Assert.Equal(1, second.NextPiece);
            Assert.Equal(LargeFileAppendOutcome.LeaseLost, Append(store, first.Lease, 1, Content(Piece)).Outcome);

            // Released: free at once; releasing a stale lease changes nothing.
            store.Release(first.Lease);
            Assert.Equal(LargeFileBeginOutcome.Busy, Begin(store, key).Outcome);
            store.Release(second.Lease);
            store.Release(second.Lease);
            Assert.Equal(LargeFileBeginOutcome.Continue, Begin(store, key).Outcome);
        }

        [Fact]
        public void Another_source_identity_name_size_or_time_is_another_upload()
        {
            var store = Open(new LargeFileStoreLimits { MaxUploadsInProgress = 10 });
            var first = Begin(store, Key("a", Piece, identity: "inode-1"));

            var ids = new[]
            {
                Begin(store, Key("a", Piece, identity: "inode-2")),
                Begin(store, Key("a", Piece, identity: null)),
                Begin(store, Key("A", Piece, identity: "inode-1")),
                Begin(store, Key("a", Piece + 1, identity: "inode-1")),
                Begin(store, Key("a", Piece, identity: "inode-1", modified: T0)),
            };

            Assert.All(ids, r => Assert.Equal(LargeFileBeginOutcome.Started, r.Outcome));
            Assert.Equal(6, ids.Select(r => r.FileId).Append(first.FileId).Distinct().Count());
        }

        [Fact]
        public void A_begin_with_another_piece_size_discards_the_old_part_and_starts_with_a_new_file_id()
        {
            var store = Open();
            var key = Key("a", 4 * Piece);
            var first = Begin(store, key);
            Append(store, first.Lease, 0, Content(Piece));
            store.Release(first.Lease);

            var second = store.Begin(key, null, 2 * Piece, 2 * Piece);

            Assert.Equal(LargeFileBeginOutcome.Started, second.Outcome);
            Assert.NotEqual(first.FileId, second.FileId);
            Assert.Equal(2, second.PieceCount);
            Assert.Equal(2 * Piece, second.PieceSize);
            Assert.Null(store.TryGetFile(first.FileId));
            Assert.False(File.Exists(Path.Combine(_root, "uploads", _product.ToString("N"), $"{first.FileId:N}.part")));
            Assert.Equal(1, store.Statistics.PartsRemoved);
        }

        [Fact]
        public void A_file_being_checked_answers_checking_to_a_begin_and_to_a_second_finish_and_keeps_its_lease()
        {
            var store = Open();
            var key = Key("a", Piece);
            var begin = Begin(store, key);
            Append(store, begin.Lease, 0, Content(Piece));

            Assert.Equal(LargeFileFinishStart.Ready, store.StartFinish(begin.Lease).Outcome);

            // No lease timeout during the check.
            _now = _now.AddHours(1);
            Assert.Equal(LargeFileBeginOutcome.Checking, Begin(store, key).Outcome);
            Assert.Equal(LargeFileFinishStart.Checking, store.StartFinish(begin.Lease).Outcome);
            Assert.False(store.Remove(begin.FileId, "test"));

            // The end of the check ends the lease too, whatever came of it: the part is never left busy.
            store.EndFinish(begin.Lease);
            Assert.Equal(LargeFileAppendOutcome.LeaseLost, Append(store, begin.Lease, 0, Content(Piece)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.Continue, Begin(store, key).Outcome);
        }

        [Fact]
        public void A_discarded_upload_is_gone_with_its_reservation_and_its_appends_are_answered_discarded()
        {
            var store = Open();
            var begin = Begin(store, Key("a", 2 * Piece));
            Append(store, begin.Lease, 0, Content(Piece));

            store.Discard(begin.Lease, "a bad piece");
            store.Discard(begin.Lease, "again");

            Assert.Null(store.TryGetFile(begin.FileId));
            Assert.Equal(LargeFileAppendOutcome.Discarded, Append(store, begin.Lease, 1, Content(Piece)).Outcome);
            Assert.Equal(LargeFileFinishStart.Discarded, store.StartFinish(begin.Lease).Outcome);
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "uploads", _product.ToString("N"))));
            Assert.Equal(1, store.Statistics.PartsRemoved);
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("a", 2 * Piece)).Outcome);
        }

        // === Limits and free space ===

        [Fact]
        public void The_number_of_uploads_in_progress_is_the_number_of_live_leases()
        {
            var store = Open(new LargeFileStoreLimits { MaxUploadsInProgress = 2 });

            var a = Begin(store, Key("a", 2 * Piece));
            Append(store, a.Lease, 0, Content(Piece));
            var b = Begin(store, Key("b", Piece));
            var refused = Begin(store, Key("c", Piece));

            Assert.Equal(LargeFileBeginOutcome.TooManyUploads, refused.Outcome);
            Assert.NotNull(refused.RetryAfter);

            // A continue counts too.
            store.Release(a.Lease);
            Assert.Equal(LargeFileBeginOutcome.Continue, Begin(store, Key("a", 2 * Piece)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.TooManyUploads, Begin(store, Key("c", Piece)).Outcome);

            // An expired lease does not.
            _now = _now.AddMinutes(4);
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("c", Piece)).Outcome);
            Assert.NotEqual(Guid.Empty, b.FileId);
        }

        [Fact]
        public void The_largest_file_and_the_piece_sizes_are_limits()
        {
            var store = Open(new LargeFileStoreLimits { MaxFileSizeBytes = 10 * Piece });

            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("max", 10 * Piece)).Outcome);
            var tooLarge = Begin(store, Key("over", 10 * Piece + 1));
            Assert.Equal(LargeFileBeginOutcome.TooLarge, tooLarge.Outcome);
            Assert.Equal(0, tooLarge.PieceSize);
            Assert.Equal(LargeFileBeginOutcome.TooLarge, Begin(store, Key("negative", -1)).Outcome);

            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p1", Piece), null, Piece - 1, 4 * Piece).Outcome);
            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p2", Piece), null, 4 * Piece + 1, 4 * Piece).Outcome);
            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p3", Piece), null, Piece, Piece - 1).Outcome);
            Assert.Equal(LargeFileBeginOutcome.Started, store.Begin(Key("p4", Piece), null, 4 * Piece, 4 * Piece).Outcome);
        }

        [Fact]
        public void A_begin_is_refused_for_space_counting_every_reservation_so_two_begins_cannot_share_free_bytes()
        {
            const long Slack = LargeFileStore.ArchiveSlack;
            var limits = new LargeFileStoreLimits { MinFreeBytes = 1000 };
            var store = Open(limits);

            // An upload reserves its size for the part and its size + 1 MiB for its archive.
            _free = 1000 + 20 * Piece + 2 * Slack;

            var first = Begin(store, Key("a", 6 * Piece));
            Assert.Equal(LargeFileBeginOutcome.Started, first.Outcome);
            Assert.Equal(12 * Piece + Slack, store.ReservedBytes);

            // The same free bytes again: the first upload's reservation is counted.
            var second = Begin(store, Key("b", 6 * Piece));
            Assert.Equal(LargeFileBeginOutcome.NoRoom, second.Outcome);
            Assert.NotNull(second.RetryAfter);
            Assert.Equal(1, store.Statistics.RefusedForSpace);

            var third = Begin(store, Key("c", 4 * Piece));
            Assert.Equal(LargeFileBeginOutcome.Started, third.Outcome);
            Append(store, third.Lease, 0, Content(Piece));
            _free -= Piece;

            // A piece on the disk takes its bytes from the part's reservation: the disk has that much less free.
            Append(store, first.Lease, 0, Content(Piece));
            _free -= Piece;
            Assert.Equal(11 * Piece + 7 * Piece + 2 * Slack, store.ReservedBytes);

            // A restart restores the reservations from the records.
            store.Dispose();
            var reopened = Open(limits);
            Assert.Equal(18 * Piece + 2 * Slack, reopened.ReservedBytes);
            Assert.Equal(LargeFileBeginOutcome.NoRoom, Begin(reopened, Key("d", Piece)).Outcome);

            // A discard releases it.
            var continued = Begin(reopened, Key("a", 6 * Piece));
            reopened.Discard(continued.Lease, "test");
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(reopened, Key("d", Piece)).Outcome);
        }

        [Fact]
        public void A_re_encryption_reserves_room_for_its_second_archive_and_is_refused_without_it()
        {
            var store = Open(new LargeFileStoreLimits { MinFreeBytes = 0 });
            var id = Verify(store, "a", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, new byte[1000]);
            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(pack, 1, 1000, [1]));
            Assert.Equal(0, store.ReservedBytes);

            _free = 1000 + LargeFileStore.ArchiveSlack - 1;
            var refused = store.BeginArchive(id);
            Assert.Equal(ArchiveBeginOutcome.NoRoom, refused.Outcome);
            Assert.NotNull(refused.RetryAfter);
            Assert.Equal(1, store.Statistics.RefusedForSpace);

            _free = 1000 + LargeFileStore.ArchiveSlack;
            var again = Started(store.BeginArchive(id));
            Assert.Equal(1000 + LargeFileStore.ArchiveSlack, store.ReservedBytes);

            store.AbandonArchive(again);
            Assert.Equal(0, store.ReservedBytes);
        }

        // === Leases end with their upload; parts per product ===

        [Fact]
        public void A_part_that_received_no_piece_goes_with_its_lease_when_released_expired_or_restarted()
        {
            var store = Open();

            var released = Begin(store, Key("released", Piece));
            store.Release(released.Lease);
            Assert.Null(store.TryGetFile(released.FileId));
            Assert.False(File.Exists(PathOf(released.FileId, ".part")));

            // Expired: removed by the next begin (or the pass).
            var expired = Begin(store, Key("expired", Piece));
            _now = _now.AddMinutes(4);
            Begin(store, Key("other", Piece));
            Assert.Null(store.TryGetFile(expired.FileId));

            // Not after its first piece.
            var kept = Begin(store, Key("kept", 2 * Piece));
            Append(store, kept.Lease, 0, Content(Piece));
            store.Release(kept.Lease);
            Assert.NotNull(store.TryGetFile(kept.FileId));

            // A restart ends every lease.
            var live = Begin(store, Key("live", Piece));
            store.Dispose();
            var reopened = Open();
            Assert.Null(reopened.TryGetFile(live.FileId));
            Assert.NotNull(reopened.TryGetFile(kept.FileId));
            Assert.Equal(0, reopened.Statistics.StartDisagreements);
        }

        [Fact]
        public void A_verified_upload_holds_no_lease_and_a_release_frees_a_lease_whatever_the_state()
        {
            var store = Open(new LargeFileStoreLimits { MaxUploadsInProgress = 1 });
            var content = Content(Piece);
            var begin = Begin(store, Key("a", content.Length));
            Append(store, begin.Lease, 0, content);

            Assert.Equal(LargeFileFinishStart.Ready, store.StartFinish(begin.Lease).Outcome);
            store.MarkVerified(begin.FileId, SHA256.HashData(content), _now);

            // Verified before the end of the finish: no place among the uploads in progress any more.
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("b", Piece)).Outcome);
            store.Release(begin.Lease);
            store.EndFinish(begin.Lease);
        }

        [Fact]
        public void A_new_upload_is_refused_while_its_product_has_the_most_uploading_parts_and_a_continue_is_not()
        {
            var store = Open(new LargeFileStoreLimits { MaxUploadingPartsPerProduct = 2 });
            var a = Begin(store, Key("a", 2 * Piece));
            var b = Begin(store, Key("b", 2 * Piece));
            Append(store, a.Lease, 0, Content(Piece));
            Append(store, b.Lease, 0, Content(Piece));
            store.Release(a.Lease);

            var refused = Begin(store, Key("c", Piece));
            Assert.Equal(LargeFileBeginOutcome.TooManyParts, refused.Outcome);
            Assert.NotNull(refused.RetryAfter);

            Assert.Equal(LargeFileBeginOutcome.Continue, Begin(store, Key("a", 2 * Piece)).Outcome);

            // Another product is not counted, and a verified file is not a part.
            Assert.Equal(LargeFileBeginOutcome.Started, store.Begin(Key("c", Piece) with { ProductId = Guid.NewGuid() }, null, Piece, Piece).Outcome);
        }

        // === Commits that lost a race; orphans ===

        [Fact]
        public void A_commit_after_another_build_committed_is_superseded_and_one_after_a_removal_is_removed()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));

            var one = Started(store.BeginArchive(id));
            var two = Started(store.BeginArchive(id));
            File.WriteAllBytes(one.TempPath, [1]);
            File.WriteAllBytes(two.TempPath, [2]);

            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(one, 1, 1, [1]));
            Assert.Equal(ArchiveCommitOutcome.Superseded, store.CommitArchive(two, 2, 1, [2]));

            var record = store.TryGetFile(id);
            Assert.Equal(one.ArchiveId, record.ArchiveId);
            Assert.Equal(1, record.PasswordVersion);
            Assert.False(File.Exists(two.TempPath));
            Assert.Equal(0, store.ReservedBytes);

            var three = Started(store.BeginArchive(id));
            File.WriteAllBytes(three.TempPath, [3]);
            Assert.True(store.Remove(id, "test"));

            Assert.Equal(ArchiveCommitOutcome.Removed, store.CommitArchive(three, 3, 1, [3]));
            Assert.False(File.Exists(three.TempPath));
            Assert.Equal(0, store.ReservedBytes);
        }

        [Fact]
        public void A_replaced_archive_a_reader_held_is_named_by_the_record_until_the_pass_removes_it()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var first = Started(store.BeginArchive(id));
            File.WriteAllBytes(first.TempPath, [1]);
            store.CommitArchive(first, 1, 1, [1]);
            var oldPath = store.ArchivePath(store.TryGetFile(id));

            var second = Started(store.BeginArchive(id));
            File.WriteAllBytes(second.TempPath, [2]);

            // A reader without delete sharing: the old archive cannot be deleted now.
            using (File.Open(oldPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(second, 2, 1, [2]));

            if (OperatingSystem.IsWindows())
                Assert.Equal([first.ArchiveId], store.TryGetFile(id).PreviousArchiveIds);

            store.Dispose();
            var reopened = Open();
            Assert.Equal(0, reopened.Statistics.StartDisagreements);

            reopened.RunStorePass();

            Assert.False(File.Exists(oldPath));
            Assert.Empty(reopened.TryGetFile(id).PreviousArchiveIds);
        }

        [Fact]
        public void The_archives_folder_itself_is_never_removed_or_replaced_only_its_empty_product_folders()
        {
            // Caddy bind-mounts <folder>/archives read-only (#1885): the store must keep that very directory.
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, [1]);
            store.CommitArchive(pack, 1, 1, [1]);

            var archives = new DirectoryInfo(Path.Combine(_root, LargeFileStore.ArchivesFolderName));
            var created = archives.CreationTimeUtc;
            var marker = Path.Combine(archives.FullName, "kept-by-the-mount");
            File.WriteAllText(marker, "x");

            Assert.True(store.Remove(id, "test"));
            store.RunStorePass();
            _now = _now.AddDays(30);
            store.RunStorePass();

            archives.Refresh();
            Assert.True(archives.Exists);
            Assert.Equal(created, archives.CreationTimeUtc);
            Assert.True(File.Exists(marker));
            Assert.Empty(Directory.GetDirectories(archives.FullName));
        }

        // === After the review of the branch (#1883) ===

        [Fact]
        public void A_verified_file_with_a_compressed_part_reserves_only_its_archive()
        {
            // Pieces of 64 KiB stored as 100 bytes each: the part holds 200 bytes of a file of 128 KiB.
            var store = Open();
            var begin = Begin(store, Key("a", 2 * Piece));
            Assert.Equal(LargeFileAppendOutcome.Stored, store.Append(begin.Lease, 0, new byte[100], new byte[32]).Outcome);
            Assert.Equal(LargeFileAppendOutcome.Stored, store.Append(begin.Lease, 1, new byte[100], new byte[32]).Outcome);
            Assert.Equal(2 * Piece - 200 + 2 * Piece + LargeFileStore.ArchiveSlack, store.ReservedBytes);

            Assert.Equal(LargeFileFinishStart.Ready, store.StartFinish(begin.Lease).Outcome);
            store.MarkVerified(begin.FileId, new byte[32], _now);

            // Nothing more comes into the part: only the archive is reserved.
            Assert.Equal(2 * Piece + LargeFileStore.ArchiveSlack, store.ReservedBytes);
        }

        [Fact]
        public void The_files_of_a_new_upload_and_of_an_archive_are_written_outside_the_stores_lock()
        {
            var store = Open();
            var inside = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();

            store.FaultHook = point =>
            {
                if (point is LargeFileStore.FaultCreatingUpload or LargeFileStore.FaultCommittingArchive && !release.IsSet)
                {
                    inside.Set();
                    release.Wait(TimeSpan.FromSeconds(30));
                }
            };

            var first = Task.Run(() => Begin(store, Key("slow", Piece)));
            Assert.True(inside.Wait(TimeSpan.FromSeconds(30)));

            // While its files are written: another file begins, the same file is busy, nothing is listed half-made.
            store.FaultHook = null;
            var other = Begin(store, Key("other", Piece));
            Assert.Equal(LargeFileBeginOutcome.Started, other.Outcome);
            Assert.Equal(LargeFileBeginOutcome.Busy, Begin(store, Key("slow", Piece)).Outcome);
            Assert.Single(store.GetFiles());

            release.Set();
            Assert.Equal(LargeFileBeginOutcome.Started, first.Result.Outcome);
            Assert.Equal(2, store.GetFiles().Count);

            // A commit: its rename and record are written while a begin goes on.
            var id = Verify(store, "packed", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, [1]);

            inside.Reset();
            release.Reset();
            store.FaultHook = point =>
            {
                if (point == LargeFileStore.FaultCommittingArchive && !release.IsSet)
                {
                    inside.Set();
                    release.Wait(TimeSpan.FromSeconds(30));
                }
            };

            var commit = Task.Run(() => store.CommitArchive(pack, 1, 1, [1]));
            Assert.True(inside.Wait(TimeSpan.FromSeconds(30)));
            store.FaultHook = null;
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("during", Piece)).Outcome);
            store.RunStorePass();

            release.Set();
            Assert.Equal(ArchiveCommitOutcome.Committed, commit.Result);
            Assert.True(File.Exists(store.ArchivePath(store.TryGetFile(id))));
        }

        [Fact]
        public void Archives_of_a_product_without_records_or_after_values_named_unknown_files_are_kept_and_reported()
        {
            // An archives/ with files and no record (its uploads/ lost, or a folder put back): never removed as orphans.
            var lost = Path.Combine(_root, "archives", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(lost);
            var archive = Path.Combine(lost, $"{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(archive, [1]);
            File.SetLastWriteTimeUtc(archive, _now.AddDays(-3));

            var store = Open();
            Assert.Equal(1, store.Statistics.StartDisagreements);

            var report = store.RunStorePass();
            Assert.Equal(0, report.OrphanArchivesRemoved);
            Assert.Equal(1, report.OrphanArchivesKept);
            Assert.True(File.Exists(archive));

            // Values naming files the store does not know: no orphan is removed, also of a product with records.
            var id = Verify(store, "a", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, [1]);
            store.CommitArchive(pack, 1, 1, [1]);

            var stray = Path.Combine(Path.GetDirectoryName(store.ArchivePath(store.TryGetFile(id)))!, $"{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(stray, [1]);
            File.SetLastWriteTimeUtc(stray, _now.AddDays(-3));

            store.KeepOrphanArchives("a test");
            Assert.Equal(0, store.RunStorePass().OrphanArchivesRemoved);
            Assert.True(File.Exists(stray));
        }

        [Fact]
        public void The_store_has_content_only_once_it_took_a_file_whatever_its_folder()
        {
            // A bind mount creates the folder on every server.
            Directory.CreateDirectory(_root);
            var store = Open();

            Assert.True(store.Exists);
            Assert.False(store.HasContent);

            Begin(store, Key("a", Piece));
            Assert.True(store.HasContent);
        }

        [Fact]
        public void A_lease_follows_the_monotonic_clock_not_the_wall_clock()
        {
            var store = Open();
            var key = Key("a", 2 * Piece);
            var begin = Begin(store, key);

            // The wall clock steps a day forward: the lease is still live.
            _monotonicMs = _now.Ticks / TimeSpan.TicksPerMillisecond;
            _now = _now.AddDays(1);
            Assert.Equal(LargeFileBeginOutcome.Busy, Begin(store, key).Outcome);

            // The monotonic clock passes the lease timeout while the wall clock stands: expired.
            _monotonicMs += 181_000;
            Assert.Equal(LargeFileAppendOutcome.LeaseLost, Append(store, begin.Lease, 0, Content(Piece)).Outcome);
        }

        [Fact]
        public void A_store_that_cannot_open_answers_the_fault_for_a_minute_without_trying_again_and_counts_it_once()
        {
            if (!OperatingSystem.IsWindows())
                return; // the failure is made by holding a record without sharing, which Linux does not enforce

            var uploads = Path.Combine(_root, "uploads", _product.ToString("N"));
            Directory.CreateDirectory(uploads);
            var locked = Path.Combine(uploads, $"{Guid.NewGuid():N}.rec");
            File.WriteAllText(locked, "{}");

            var store = new LargeFileStore(_root) { Clock = () => _now, MonotonicMs = () => _monotonicMs ?? 0, FreeBytes = () => _free };
            _stores.Add(store);
            _monotonicMs = 1_000;

            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<LargeFileStoreException>(() => store.HasFiles(_sensor));
                Assert.Throws<LargeFileStoreException>(() => store.HasFiles(_sensor));
            }

            // Inside the backoff: the same fault, not tried again, not counted again.
            Assert.Throws<LargeFileStoreException>(() => store.GetFiles());
            Assert.Equal(1, store.Statistics.WriteFailures);

            _monotonicMs += 61_000;
            Assert.False(store.HasFiles(_sensor));
            Assert.Equal(1, store.Statistics.StartDisagreements); // the record "{}" cannot be read: reported, kept
        }

        [Fact]
        public void A_commit_that_fails_ends_its_build_its_reservation_and_leaves_its_temporary_file_to_the_pass()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, new byte[10]);
            store.CommitArchive(pack, 1, 10, [1]);

            var again = Started(store.BeginArchive(id));
            Assert.Equal(10 + LargeFileStore.ArchiveSlack, store.ReservedBytes);
            File.WriteAllBytes(again.TempPath, [2]);

            // The rename cannot happen: something already stands where the archive goes.
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(store.ArchivePath(store.TryGetFile(id)))!, $"{again.ArchiveId:N}.zip"));

            Assert.Throws<LargeFileStoreException>(() => store.CommitArchive(again, 2, 1, [2]));
            Assert.Equal(0, store.ReservedBytes);
            Assert.Equal(pack.ArchiveId, store.TryGetFile(id).ArchiveId);

            Assert.Equal(1, store.RunStorePass().LeftoversRemoved);
            Assert.False(File.Exists(again.TempPath));
        }

        [Fact]
        public void Where_a_file_came_from_is_kept_in_its_record_from_its_begin_and_an_older_record_without_it_still_reads()
        {
            var store = Open();
            var begin = store.Begin(Key("a", 2 * Piece), null, Piece, Piece, "collector-1", "10.0.0.7");
            Append(store, begin.Lease, 0, Content(Piece));
            store.Release(begin.Lease);

            // A continue from elsewhere keeps the record's origin.
            Assert.Equal(LargeFileBeginOutcome.Continue, store.Begin(Key("a", 2 * Piece), null, Piece, Piece, "collector-2", "10.0.0.8").Outcome);
            store.Dispose();

            var record = Open().TryGetFile(begin.FileId);
            Assert.Equal("collector-1", record.ClientName);
            Assert.Equal("10.0.0.7", record.RemoteAddress);

            // A record written before the members existed.
            var text = File.ReadAllText(PathOf(begin.FileId, ".rec"));
            Assert.Contains("clientName", text);
            var older = LargeFileRecordFormat.Read(text.Replace("\"clientName\":\"collector-1\",", "").Replace(",\"remoteAddress\":\"10.0.0.7\"", "").Replace("\"remoteAddress\":\"10.0.0.7\",", ""));
            Assert.Null(older.ClientName);
            Assert.Null(older.RemoteAddress);
            Assert.Equal(begin.FileId, older.FileId);
        }

        [Fact]
        public void A_file_is_being_checked_from_the_start_of_its_finish_to_its_end()
        {
            var store = Open();
            var begin = Begin(store, Key("a", Piece));
            Append(store, begin.Lease, 0, Content(Piece));

            Assert.False(store.IsChecking(begin.FileId));
            store.StartFinish(begin.Lease);
            Assert.True(store.IsChecking(begin.FileId));
            store.EndFinish(begin.Lease);
            Assert.False(store.IsChecking(begin.FileId));
            Assert.False(store.IsChecking(Guid.NewGuid()));
        }

        [Fact]
        public void An_archive_no_record_names_is_removed_by_the_pass_once_a_day_old()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var pack = Started(store.BeginArchive(id));
            File.WriteAllBytes(pack.TempPath, [1]);
            store.CommitArchive(pack, 1, 1, [1]);

            var stray = Path.Combine(Path.GetDirectoryName(store.ArchivePath(store.TryGetFile(id)))!, $"{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(stray, [1]);
            File.SetLastWriteTimeUtc(stray, _now.AddHours(-23));

            var kept = store.RunStorePass();
            Assert.Equal(1, kept.OrphanArchivesKept);
            Assert.True(File.Exists(stray));

            _now = _now.AddHours(2);
            var removed = store.RunStorePass();

            Assert.Equal(1, removed.OrphanArchivesRemoved);
            Assert.False(File.Exists(stray));
            Assert.True(File.Exists(store.ArchivePath(store.TryGetFile(id))));
            Assert.Equal(1, store.Statistics.LeftoversRemoved);
        }

        // === A cut at every point of an append, then a continue ===

        [Theory]
        [InlineData(LargeFileStore.FaultPartWritten, 1)]
        [InlineData(LargeFileStore.FaultPartFlushed, 1)]
        [InlineData(LargeFileStore.FaultIndexWritten, 1)]
        [InlineData(LargeFileStore.FaultIndexFlushed, 1)]
        [InlineData(LargeFileStore.FaultRecordWritten, 2)]
        public void A_cut_at_any_point_of_an_append_keeps_every_acknowledged_piece_and_the_upload_continues(string point, long nextAfter)
        {
            var store = Open();
            var content = Content(3 * Piece + 7);
            var begin = Begin(store, Key("a", content.Length));
            Append(store, begin.Lease, 0, content.AsSpan(0, Piece).ToArray());

            store.FaultHook = name =>
            {
                if (name == point)
                    throw new SimulatedCrash();
            };

            Assert.Throws<SimulatedCrash>(() => Append(store, begin.Lease, 1, content.AsSpan(Piece, Piece).ToArray()));

            // The process dies here: the next start reads the folder.
            store.Dispose();
            var reopened = Open();
            var again = Begin(reopened, Key("a", content.Length));

            Assert.Equal(LargeFileBeginOutcome.Continue, again.Outcome);
            Assert.Equal(nextAfter, again.NextPiece);

            var uploads = Path.Combine(_root, "uploads", _product.ToString("N"));
            Assert.Equal(nextAfter * Piece, new FileInfo(Path.Combine(uploads, $"{begin.FileId:N}.part")).Length);
            Assert.Equal(nextAfter * LargeFileStore.IndexEntrySize, new FileInfo(Path.Combine(uploads, $"{begin.FileId:N}.idx")).Length);

            AppendAll(reopened, again.Lease, content, from: (int)nextAfter);
            var pieces = ReadPieces(reopened.StartFinish(again.Lease).Reader);

            Assert.Equal(content, pieces.SelectMany(p => p.Bytes.ToArray()));
            Assert.All(pieces, p => Assert.Equal(Digest(p.Bytes.Span), p.Digest.ToArray()));
        }

        [Fact]
        public void A_part_longer_than_its_record_is_cut_back_and_counted()
        {
            var store = Open();
            var begin = Begin(store, Key("a", 3 * Piece));
            Append(store, begin.Lease, 0, Content(Piece));
            store.Dispose();

            var part = PathOf(begin.FileId, ".part");
            var idx = PathOf(begin.FileId, ".idx");
            File.AppendAllText(part, "tail that was never acknowledged");
            File.AppendAllText(idx, "x");

            var reopened = Open();

            Assert.Equal(Piece, new FileInfo(part).Length);
            Assert.Equal(LargeFileStore.IndexEntrySize, new FileInfo(idx).Length);
            Assert.Equal(1, reopened.Statistics.StartDisagreements);
            Assert.Equal(1, Begin(reopened, Key("a", 3 * Piece)).NextPiece);
        }

        [Fact]
        public void A_part_shorter_than_its_record_is_cut_back_to_its_last_whole_piece_and_the_record_rewritten()
        {
            var store = Open();
            var content = Content(4 * Piece);
            var begin = Begin(store, Key("a", content.Length));
            AppendAll(store, begin.Lease, content, to: 3);
            store.Dispose();

            // The disk lost the end of the part (half of piece 2).
            using (var stream = new FileStream(PathOf(begin.FileId, ".part"), FileMode.Open))
                stream.SetLength(2 * Piece + Piece / 2);

            var reopened = Open();
            var record = reopened.TryGetFile(begin.FileId);

            Assert.Equal(2, record.PieceCount);
            Assert.Equal(2 * Piece, record.PartLength);
            Assert.Equal(2 * Piece, new FileInfo(PathOf(begin.FileId, ".part")).Length);
            Assert.Equal(2 * LargeFileStore.IndexEntrySize, new FileInfo(PathOf(begin.FileId, ".idx")).Length);
            Assert.Equal(1, reopened.Statistics.StartDisagreements);

            // The record on the disk says so too.
            reopened.Dispose();
            Assert.Equal(2, Open().TryGetFile(begin.FileId).PieceCount);
        }

        [Fact]
        public void An_uploading_record_without_its_part_is_removed_and_a_verified_one_is_reported_and_kept()
        {
            var store = Open();
            var uploading = Begin(store, Key("u", 2 * Piece));
            Append(store, uploading.Lease, 0, Content(Piece));
            var verified = Verify(store, "v", Content(Piece));
            store.Dispose();

            File.Delete(PathOf(uploading.FileId, ".part"));
            File.Delete(PathOf(verified, ".part"));

            var reopened = Open();

            Assert.Null(reopened.TryGetFile(uploading.FileId));
            Assert.False(File.Exists(PathOf(uploading.FileId, ".rec")));
            Assert.Equal(LargeFileState.Verified, reopened.TryGetFile(verified).State);
            Assert.True(File.Exists(PathOf(verified, ".rec")));
            Assert.Equal(2, reopened.Statistics.StartDisagreements);
            Assert.Equal(1, reopened.Statistics.PartsRemoved);
        }

        [Fact]
        public void A_damaged_record_is_reported_and_its_files_are_kept()
        {
            var store = Open();
            var begin = Begin(store, Key("a", Piece));
            store.Dispose();

            File.WriteAllText(PathOf(begin.FileId, ".rec"), "{ not a record");

            var reopened = Open();

            Assert.Null(reopened.TryGetFile(begin.FileId));
            Assert.True(File.Exists(PathOf(begin.FileId, ".part")));
            Assert.True(File.Exists(PathOf(begin.FileId, ".rec")));
            Assert.Equal(1, reopened.Statistics.StartDisagreements);
        }

        [Fact]
        public void A_temporary_record_left_by_a_crash_is_deleted_and_the_record_before_it_stands()
        {
            var store = Open();
            var begin = Begin(store, Key("a", Piece));
            Append(store, begin.Lease, 0, Content(Piece));
            store.Dispose();

            File.WriteAllText(PathOf(begin.FileId, ".rec.tmp"), "half");

            var reopened = Open();

            Assert.Equal(LargeFileState.Uploading, reopened.TryGetFile(begin.FileId).State);
            Assert.False(File.Exists(PathOf(begin.FileId, ".rec.tmp")));
        }

        // === The store's own pass ===

        [Fact]
        public void An_uploading_part_is_removed_seven_days_after_its_last_piece_and_a_verified_one_never()
        {
            var store = Open();
            var abandoned = Begin(store, Key("old", 2 * Piece));
            Append(store, abandoned.Lease, 0, Content(Piece));
            var verified = Verify(store, "kept", Content(Piece));
            store.Release(abandoned.Lease);

            _now = _now.AddDays(7).AddMinutes(-1);
            Assert.Equal(0, store.RunStorePass().AbandonedPartsRemoved);

            _now = _now.AddMinutes(2);
            var report = store.RunStorePass();

            Assert.Equal(1, report.AbandonedPartsRemoved);
            Assert.Null(store.TryGetFile(abandoned.FileId));
            Assert.False(File.Exists(PathOf(abandoned.FileId, ".part")));
            Assert.Equal(LargeFileState.Verified, store.TryGetFile(verified).State);
            Assert.Equal(1, store.Statistics.PartsRemoved);

            _now = _now.AddDays(365);
            store.RunStorePass();
            Assert.NotNull(store.TryGetFile(verified));
        }

        [Fact]
        public void A_part_without_a_record_is_kept_until_its_file_is_seven_days_old()
        {
            var store = Open();
            var begin = Begin(store, Key("a", Piece));
            store.Dispose();
            File.Delete(PathOf(begin.FileId, ".rec"));

            var reopened = Open();
            File.SetLastWriteTimeUtc(PathOf(begin.FileId, ".part"), _now.AddDays(-6));
            File.SetLastWriteTimeUtc(PathOf(begin.FileId, ".idx"), _now.AddDays(-6));

            var kept = reopened.RunStorePass();
            Assert.Equal(1, kept.OrphanPartsKept);
            Assert.True(File.Exists(PathOf(begin.FileId, ".part")));

            _now = _now.AddDays(1).AddMinutes(1);
            var removed = reopened.RunStorePass();

            Assert.Equal(1, removed.OrphanPartsRemoved);
            Assert.False(File.Exists(PathOf(begin.FileId, ".part")));
            Assert.False(File.Exists(PathOf(begin.FileId, ".idx")));
            Assert.Equal(1, reopened.Statistics.PartsRemoved);

            // The empty product folder goes too.
            Assert.False(Directory.Exists(Path.Combine(_root, "uploads", _product.ToString("N"))));
        }

        [Fact]
        public void Removing_a_verified_file_removes_its_files_and_record_and_counts_it()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));

            Assert.True(store.Remove(id, "its value is gone"));
            Assert.False(store.Remove(id, "again"));

            Assert.Null(store.TryGetFile(id));
            Assert.False(store.HasFiles(_sensor));
            Assert.False(File.Exists(PathOf(id, ".part")));
            Assert.False(File.Exists(PathOf(id, ".rec")));
            Assert.Equal(1, store.Statistics.FilesRemoved);
            Assert.Equal(0, store.ReservedBytes);
        }

        // === Archives (#1885) ===

        [Fact]
        public void A_committed_archive_packs_the_file_removes_its_part_and_releases_the_reservation()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            // Until its first pack a verified file reserves room for its archive (its part holds every byte already).
            Assert.Equal(Piece + LargeFileStore.ArchiveSlack, store.ReservedBytes);

            Assert.Throws<LargeFileStoreException>(() => store.OpenArchive(id));

            var target = Started(store.BeginArchive(id));
            Assert.Null(target.ExpectedArchiveId);
            Assert.Equal(Path.Combine(_root, "uploads", _product.ToString("N"), $"{target.ArchiveId:N}.zip.tmp"), target.TempPath);
            File.WriteAllBytes(target.TempPath, [1, 2, 3]);

            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(target, passwordVersion: 1, archiveLength: 3, archiveHash: [9]));

            var record = store.TryGetFile(id);
            Assert.Equal(LargeFileState.Packed, record.State);
            Assert.Equal(target.ArchiveId, record.ArchiveId);
            Assert.Equal(1, record.PasswordVersion);
            Assert.Equal(3, record.ArchiveLength);
            Assert.Equal(Path.Combine(_root, "archives", _product.ToString("N"), $"{target.ArchiveId:N}.zip"), store.ArchivePath(record));
            Assert.False(File.Exists(target.TempPath));
            Assert.False(File.Exists(PathOf(id, ".part")));
            Assert.False(File.Exists(PathOf(id, ".idx")));
            Assert.True(File.Exists(PathOf(id, ".rec")));
            Assert.Throws<LargeFileStoreException>(() => store.OpenPart(id));
            Assert.Single(store.GetFiles(LargeFileState.Packed));
            Assert.Empty(store.GetFiles(LargeFileState.Verified));

            Assert.Equal(0, store.ReservedBytes);

            using (var archive = store.OpenArchive(id))
            using (var copy = new MemoryStream())
            {
                archive.CopyTo(copy);
                Assert.Equal([1, 2, 3], copy.ToArray());
            }

            store.Dispose();
            Assert.Equal(LargeFileState.Packed, Open().TryGetFile(id).State);
        }

        [Fact]
        public void A_re_encryption_swaps_the_archive_and_deletes_the_old_one_under_an_open_reader()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var first = Started(store.BeginArchive(id));
            File.WriteAllBytes(first.TempPath, [1, 1, 1]);
            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(first, 1, 3, [1]));
            var oldPath = store.ArchivePath(store.TryGetFile(id));

            using var reader = store.OpenArchive(id);

            var second = Started(store.BeginArchive(id));
            Assert.Equal(first.ArchiveId, second.ExpectedArchiveId);
            Assert.NotEqual(first.ArchiveId, second.ArchiveId);
            File.WriteAllBytes(second.TempPath, [2, 2, 2, 2]);
            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(second, 2, 4, [2]));

            var record = store.TryGetFile(id);
            Assert.Equal(second.ArchiveId, record.ArchiveId);
            // Deleted under the open reader (it shares delete): the record names no replaced archive any more.
            Assert.Empty(record.PreviousArchiveIds);
            Assert.Equal(2, record.PasswordVersion);

            // The reader that opened the old archive reads it to its end.
            var buffer = new byte[3];
            reader.ReadExactly(buffer);
            Assert.Equal([1, 1, 1], buffer);
            reader.Dispose();

            Assert.False(File.Exists(oldPath));
            store.RunStorePass();
            Assert.False(File.Exists(oldPath));
        }

        [Fact]
        public void An_abandoned_build_deletes_its_temporary_file_and_keeps_the_part()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var target = Started(store.BeginArchive(id));
            File.WriteAllBytes(target.TempPath, [1]);

            store.AbandonArchive(target);

            Assert.False(File.Exists(target.TempPath));
            Assert.Equal(LargeFileState.Verified, store.TryGetFile(id).State);
            Assert.True(File.Exists(PathOf(id, ".part")));
        }

        [Fact]
        public void An_archive_cannot_be_built_for_an_upload_and_the_pass_removes_the_leftovers_of_a_crashed_build()
        {
            var store = Open();
            var uploading = Begin(store, Key("u", Piece));
            Append(store, uploading.Lease, 0, Content(Piece));
            Assert.Equal(ArchiveBeginOutcome.WrongState, store.BeginArchive(uploading.FileId).Outcome);
            Assert.Equal(ArchiveBeginOutcome.NotFound, store.BeginArchive(Guid.NewGuid()).Outcome);

            var id = Verify(store, "a", Content(Piece));
            var target = Started(store.BeginArchive(id));
            File.WriteAllBytes(target.TempPath, [1]);

            // A build running: the pass leaves its temporary file alone.
            store.RunStorePass();
            Assert.True(File.Exists(target.TempPath));

            // A crash: after the restart nothing builds it.
            store.Dispose();
            var reopened = Open();
            Assert.Equal(1, reopened.RunStorePass().LeftoversRemoved);
            Assert.False(File.Exists(target.TempPath));
        }

        [Fact]
        public void An_archive_without_a_record_and_a_packed_record_without_its_archive_are_reported()
        {
            var store = Open();
            var id = Verify(store, "a", Content(Piece));
            var target = Started(store.BeginArchive(id));
            File.WriteAllBytes(target.TempPath, [1]);
            Assert.Equal(ArchiveCommitOutcome.Committed, store.CommitArchive(target, 1, 1, [1]));
            var archive = store.ArchivePath(store.TryGetFile(id));
            store.Dispose();

            var stray = Path.Combine(Path.GetDirectoryName(archive)!, $"{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(stray, [1]);
            File.Delete(archive);

            var reopened = Open();

            Assert.Equal(2, reopened.Statistics.StartDisagreements);
            Assert.True(File.Exists(stray));
            Assert.Equal(LargeFileState.Packed, reopened.TryGetFile(id).State);
        }

        [Fact]
        public void The_verified_event_is_raised_when_published_and_a_failing_handler_does_not_break_it()
        {
            var store = Open();
            var seen = new List<LargeFileRecord>();
            store.FileVerified += _ => throw new InvalidOperationException("a handler that fails");
            store.FileVerified += seen.Add;

            var id = Verify(store, "a", Content(Piece));
            store.PublishVerified(id);

            Assert.Single(seen);
            Assert.Equal(LargeFileState.Verified, seen[0].State);
        }


        // === Helpers ===

        internal sealed class SimulatedCrash : Exception;

        private LargeFileStore Open(LargeFileStoreLimits limits = null)
        {
            var store = new LargeFileStore(_root)
            {
                Clock = () => _now,
                MonotonicMs = () => _monotonicMs ?? _now.Ticks / TimeSpan.TicksPerMillisecond,
                FreeBytes = () => _free,
                Limits = limits ?? new LargeFileStoreLimits(),
            };

            _stores.Add(store);
            store.EnsureOpened();

            return store;
        }

        private LargeFileUploadKey Key(string name, long size, string identity = null, DateTime? modified = null) =>
            new(_product, _sensor, name, size, modified ?? T0.AddDays(-1), identity);

        private static LargeFileBeginResult Begin(LargeFileStore store, LargeFileUploadKey key) =>
            store.Begin(key, Path.GetExtension(key.Name).TrimStart('.'), Piece, Piece);

        private static LargeFileAppendResult Append(LargeFileStore store, UploadLease lease, long number, byte[] bytes) =>
            store.Append(lease, number, bytes, Digest(bytes));

        private static void AppendAll(LargeFileStore store, UploadLease lease, byte[] content, int from = 0, int? to = null)
        {
            var count = (content.Length + Piece - 1) / Piece;

            for (var i = from; i < (to ?? count); i++)
            {
                var piece = content.AsSpan(i * Piece, Math.Min(Piece, content.Length - i * Piece)).ToArray();
                Assert.Equal(LargeFileAppendOutcome.Stored, Append(store, lease, i, piece).Outcome);
            }
        }

        private Guid Verify(LargeFileStore store, string name, byte[] content)
        {
            var begin = Begin(store, Key(name, content.Length));
            AppendAll(store, begin.Lease, content);
            Assert.Equal(LargeFileFinishStart.Ready, store.StartFinish(begin.Lease).Outcome);
            store.MarkVerified(begin.FileId, SHA256.HashData(content), _now);
            store.EndFinish(begin.Lease);
            store.Release(begin.Lease);

            return begin.FileId;
        }

        private static List<StoredPiece> ReadPieces(ILargeFilePartReader reader)
        {
            var pieces = new List<StoredPiece>();

            var enumerator = reader.ReadPiecesAsync(CancellationToken.None).GetAsyncEnumerator();

            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    pieces.Add(enumerator.Current);
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return pieces;
        }

        private static ArchiveTarget Started(ArchiveBeginResult result)
        {
            Assert.Equal(ArchiveBeginOutcome.Started, result.Outcome);
            return result.Target;
        }

        private string PathOf(Guid fileId, string suffix) => Path.Combine(_root, "uploads", _product.ToString("N"), $"{fileId:N}{suffix}");

        private static byte[] Digest(ReadOnlySpan<byte> bytes) => SHA256.HashData(bytes);

        private static byte[] Content(int length)
        {
            var bytes = new byte[length];
            new Random(length).NextBytes(bytes);
            return bytes;
        }
    }
}
