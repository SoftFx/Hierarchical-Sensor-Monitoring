using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBlocks;
using HSMTableBlocks;
using LevelDB;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.TableBlockTests
{
    /// <summary>What the block store gives the merge (#1850): closing an hour, deleting a merged hour, watermarks.</summary>
    public sealed class TableBlockMergeTests : IDisposable
    {
        private static readonly DateTime T0 = new(2026, 11, 24, 12, 10, 0, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-table-merge-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _producer = Guid.NewGuid();
        private readonly List<TableBlockDatabase> _stores = [];
        private DateTime _now = T0;

        private long Hour0 => TableBlockDatabase.FloorHour(T0);


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


        [Fact]
        public void After_an_hour_is_closed_no_block_is_appended_to_it_also_when_the_clock_is_behind()
        {
            var store = Open();
            Put(store, 0);
            store.CloseHour(Hour0);

            Put(store, 1);
            _now = T0.AddHours(-3);
            Put(store, 2);

            Assert.Equal([Hour0, Hour0 + TimeSpan.TicksPerHour, Hour0 + TimeSpan.TicksPerHour], store.GetBlocks(_product).Select(e => e.ArrivalHourTicks));

            // Closing an earlier hour does not take it back.
            store.CloseHour(Hour0 - 5 * TimeSpan.TicksPerHour);
            _now = T0;
            Put(store, 3);
            Assert.Equal(Hour0 + TimeSpan.TicksPerHour, store.GetBlocks(_product)[^1].ArrivalHourTicks);
        }

        [Fact]
        public void A_merged_hour_is_deleted_except_the_kept_blocks_and_its_resends_are_duplicates_by_the_watermark()
        {
            var store = Open();
            for (int i = 0; i < 4; i++)
                Put(store, i);
            _now = T0.AddHours(1);
            Put(store, 4);

            var deleted = store.DeleteMergedHour(Hour0, keep: [2L], [new TableBlockWatermark(_product, _producer, 3)]);

            Assert.Equal(3, deleted);
            Assert.Equal([2L, 4], store.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Equal(2, store.BlockCount);
            Assert.Equal(3L, store.GetWatermark(_product, _producer));

            // A resend of a deleted block, and of the kept one: duplicates; the merged one without its sequence and refusals.
            var resent = Put(store, 1);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, resent.Outcome);
            Assert.True(resent.Merged);
            Assert.Null(resent.Envelope);

            Assert.Equal(TableBlockAppendOutcome.Duplicate, Put(store, 2).Outcome);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, Put(store, 4).Outcome);
            Assert.False(Put(store, 4).Merged);

            // Another producer and another product are not covered by the watermark.
            Assert.Equal(TableBlockAppendOutcome.Stored, Append(store, 1, producer: Guid.NewGuid()).Outcome);
            Assert.Equal(TableBlockAppendOutcome.Stored, Append(store, 1, product: Guid.NewGuid()).Outcome);
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, 5).Outcome);

            // The kept block, the next hour's, the other producer's and the new one (duplicates use sequences up too).
            var live = store.GetBlocks(_product).Select(e => e.Sequence).ToList();
            Assert.Equal(4, live.Count);
            Assert.Equal([2L, 4], live.Take(2));
            store.Dispose();
            Assert.Equal(live, Open().GetBlocks(_product).Select(e => e.Sequence));
        }

        [Fact]
        public void Watermarks_only_grow_and_are_raised_without_an_open_store()
        {
            var store = Open();
            store.RaiseWatermarks([new TableBlockWatermark(_product, _producer, 10)]);
            store.RaiseWatermarks([new TableBlockWatermark(_product, _producer, 4)]);

            Assert.Equal(10L, store.GetWatermark(_product, _producer));
            Assert.False(store.IsOpened);
            Assert.Null(store.GetWatermark(_product, Guid.NewGuid()));

            Assert.True(Put(store, 10).Merged);
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, 11).Outcome);
        }

        [Fact]
        public void A_crash_between_the_deletes_of_the_blocks_and_of_their_index_entries_is_completed_by_a_repeat()
        {
            var store = Open();
            for (int i = 0; i < 3; i++)
                Put(store, i);
            _now = T0.AddHours(1);
            Put(store, 3);

            store.FaultHook = point =>
            {
                if (point == "after-hour-blocks-deleted")
                    throw new IOException("crash");
            };

            Assert.Throws<TableBlockStoreException>(() => store.DeleteMergedHour(Hour0, [], []));
            store.Dispose();

            // The blocks are gone, their index entries are not: the reopened store lists them and their reads fail.
            var reopened = Open();
            Assert.Equal([0L, 1, 2, 3], reopened.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Null(reopened.ReadBlock(reopened.GetBlocks(_product)[0]));

            Assert.Equal(3, reopened.DeleteMergedHour(Hour0, [], []));
            Assert.Equal([3L], reopened.GetBlocks(_product).Select(e => e.Sequence));
            reopened.Dispose();

            Assert.Equal([3L], Open().GetBlocks(_product).Select(e => e.Sequence));
        }

        [Fact]
        public void A_crash_before_the_compaction_of_a_deleted_hour_leaves_the_deletes_and_a_repeat_compacts()
        {
            var store = Open();
            for (int i = 0; i < 3; i++)
                Put(store, i);
            _now = T0.AddHours(1);
            Put(store, 3);

            store.FaultHook = point =>
            {
                if (point == "before-hour-compaction")
                    throw new IOException("crash");
            };

            Assert.Throws<IOException>(() => store.DeleteMergedHour(Hour0, [], [new TableBlockWatermark(_product, _producer, 2)]));
            store.Dispose();

            var reopened = Open();
            Assert.Equal([3L], reopened.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Equal(0, reopened.DeleteMergedHour(Hour0, [], []));
        }

        [Fact]
        public void The_products_with_blocks_are_listed_and_a_store_without_a_folder_lists_none_and_creates_nothing()
        {
            var store = Open();
            Assert.Empty(store.GetProducts());
            Assert.False(Directory.Exists(_root));

            var other = Guid.NewGuid();
            Put(store, 1);
            Append(store, 1, product: other);

            Assert.Equal(new[] { _product, other }.OrderBy(p => p), store.GetProducts());
            store.Dispose();

            Assert.Equal(new[] { _product, other }.OrderBy(p => p), Open().GetProducts());
        }

        [Fact]
        public void An_IO_error_of_the_read_is_a_fault_for_the_merge_and_a_corruption_is_damage_for_both()
        {
            var store = Open();
            Put(store, 1);
            var entry = store.GetBlocks(_product)[0];
            store.BeforeBlockGet = _ => throw new Exception("IO error: 000005.ldb: the disk did not answer");

            Assert.Throws<TableBlockStoreException>(() => store.ReadBlock(entry, readErrorIsFault: true));
            Assert.Equal(0, store.Statistics.ReadFailures);

            Assert.Null(store.ReadBlock(entry));
            Assert.Equal(1, store.Statistics.ReadFailures);

            // Any other status is permanent: damage for the merge as for a reader.
            store.BeforeBlockGet = _ => throw new Exception("Corruption: block checksum mismatch");
            Assert.Null(store.ReadBlock(entry, readErrorIsFault: true));
            Assert.Equal(2, store.Statistics.ReadFailures);

            // A block that is not there is damage for both.
            store.BeforeBlockGet = null;
            Assert.Null(store.ReadBlock(entry with { Sequence = 999 }, readErrorIsFault: true));
        }

        [Fact]
        public void Deleting_an_hour_of_a_store_never_opened_only_raises_the_watermarks()
        {
            var store = Open();

            Assert.Equal(0, store.DeleteMergedHour(Hour0, [], [new TableBlockWatermark(_product, _producer, 7)]));
            Assert.False(Directory.Exists(_root));
            Assert.Equal(7L, store.GetWatermark(_product, _producer));
        }


        private TableBlockDatabase Open()
        {
            var store = new TableBlockDatabase(_root) { Clock = () => _now };
            _stores.Add(store);
            return store;
        }

        private TableBlockAppendResult Put(TableBlockDatabase store, long producerSequence) => Append(store, producerSequence);

        private TableBlockAppendResult Append(TableBlockDatabase db, long producerSequence, Guid? producer = null, Guid? product = null)
        {
            var block = TableBlockDatabaseTests.Block(("a", 2));
            var header = TableBlockReader.ReadHeader(block);
            var ticket = db.Reserve();

            try
            {
                return db.Append(ticket, new TableBlockEnvelope { ProductId = product ?? _product, ProducerId = producer ?? _producer, ProducerSequence = producerSequence, Refusals = [] },
                    block, header.FirstTime, header.LastTime, header.RowCount, header.TableCount);
            }
            finally
            {
                if (!ticket.IsDone)
                    db.Skip(ticket);
            }
        }
    }
}
