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

            Assert.Equal(LargeFileBeginOutcome.AlreadyStored, Begin(reopened, Key("app.dmp", content.Length)).Outcome);
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

            store.EndFinish(begin.Lease);
            Assert.Equal(LargeFileBeginOutcome.Busy, Begin(store, key).Outcome);
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

            var a = Begin(store, Key("a", Piece));
            var b = Begin(store, Key("b", Piece));
            var refused = Begin(store, Key("c", Piece));

            Assert.Equal(LargeFileBeginOutcome.TooManyUploads, refused.Outcome);
            Assert.NotNull(refused.RetryAfter);

            // A continue counts too.
            store.Release(a.Lease);
            Assert.Equal(LargeFileBeginOutcome.Continue, Begin(store, Key("a", Piece)).Outcome);
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
            Assert.Equal(LargeFileBeginOutcome.TooLarge, Begin(store, Key("over", 10 * Piece + 1)).Outcome);
            Assert.Equal(LargeFileBeginOutcome.TooLarge, Begin(store, Key("negative", -1)).Outcome);

            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p1", Piece), null, Piece - 1, 4 * Piece).Outcome);
            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p2", Piece), null, 4 * Piece + 1, 4 * Piece).Outcome);
            Assert.Equal(LargeFileBeginOutcome.BadPieceSize, store.Begin(Key("p3", Piece), null, Piece, Piece - 1).Outcome);
            Assert.Equal(LargeFileBeginOutcome.Started, store.Begin(Key("p4", Piece), null, 4 * Piece, 4 * Piece).Outcome);
        }

        [Fact]
        public void A_begin_is_refused_for_space_counting_every_reservation_so_two_begins_cannot_share_free_bytes()
        {
            var limits = new LargeFileStoreLimits { MinFreeBytes = 1000 };
            var store = Open(limits);
            _free = 1000 + 10 * Piece;

            var first = Begin(store, Key("a", 6 * Piece));
            Assert.Equal(LargeFileBeginOutcome.Started, first.Outcome);

            // The same free bytes again: the first upload's reservation is counted.
            var second = Begin(store, Key("b", 6 * Piece));
            Assert.Equal(LargeFileBeginOutcome.NoRoom, second.Outcome);
            Assert.NotNull(second.RetryAfter);
            Assert.Equal(1, store.Statistics.RefusedForSpace);
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(store, Key("c", 4 * Piece)).Outcome);

            // A piece on the disk takes its bytes from the reservation: the disk has that much less free.
            Append(store, first.Lease, 0, Content(Piece));
            _free -= Piece;
            Assert.Equal(5 * Piece + 4 * Piece, store.ReservedBytes);

            // A restart restores the reservations from the records.
            store.Dispose();
            var reopened = Open(limits);
            Assert.Equal(9 * Piece, reopened.ReservedBytes);
            Assert.Equal(LargeFileBeginOutcome.NoRoom, Begin(reopened, Key("d", Piece)).Outcome);

            // A discard releases it.
            var continued = Begin(reopened, Key("a", 6 * Piece));
            reopened.Discard(continued.Lease, "test");
            Assert.Equal(LargeFileBeginOutcome.Started, Begin(reopened, Key("d", Piece)).Outcome);
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
            var uploading = Begin(store, Key("u", Piece));
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
            Assert.Equal(0, store.ReservedBytes); // a verified file reserves what its part does not hold yet: here nothing

            Assert.Throws<LargeFileStoreException>(() => store.OpenArchive(id));

            var target = store.BeginArchive(id);
            Assert.Equal(Path.Combine(_root, "uploads", _product.ToString("N"), $"{target.ArchiveId:N}.zip.tmp"), target.TempPath);
            File.WriteAllBytes(target.TempPath, [1, 2, 3]);

            store.CommitArchive(target, passwordVersion: 1, archiveLength: 3, archiveHash: [9]);

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
            var first = store.BeginArchive(id);
            File.WriteAllBytes(first.TempPath, [1, 1, 1]);
            store.CommitArchive(first, 1, 3, [1]);
            var oldPath = store.ArchivePath(store.TryGetFile(id));

            using var reader = store.OpenArchive(id);

            var second = store.BeginArchive(id);
            Assert.NotEqual(first.ArchiveId, second.ArchiveId);
            File.WriteAllBytes(second.TempPath, [2, 2, 2, 2]);
            store.CommitArchive(second, 2, 4, [2]);

            var record = store.TryGetFile(id);
            Assert.Equal(second.ArchiveId, record.ArchiveId);
            Assert.Equal(first.ArchiveId, record.PreviousArchiveId);
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
            var target = store.BeginArchive(id);
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
            Assert.Throws<LargeFileStoreException>(() => store.BeginArchive(uploading.FileId));

            var id = Verify(store, "a", Content(Piece));
            var target = store.BeginArchive(id);
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
            var target = store.BeginArchive(id);
            File.WriteAllBytes(target.TempPath, [1]);
            store.CommitArchive(target, 1, 1, [1]);
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
