using System.Text;
using System.Text.RegularExpressions;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBlocks;
using HSMTableBlocks;
using LevelDB;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.TableBlockTests
{
    /// <summary>The block store of table sensors (#1844): keys, order, duplicates, crash and index recovery, the disk guard, LevelDB's compactions.</summary>
    public sealed class TableBlockDatabaseTests : IDisposable
    {
        private static readonly DateTime T0 = new(2026, 11, 24, 12, 59, 0, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-table-blocks-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _producer = Guid.NewGuid();
        private readonly List<TableBlockDatabase> _stores = [];

        private DateTime _now = T0;


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
        public void Nothing_is_created_or_opened_before_first_use()
        {
            var store = Open();

            Assert.Empty(store.GetBlocks(_product));
            Assert.Equal(0, store.BlockCount);
            Assert.False(store.IsOpened);
            Assert.False(store.Exists);
            Assert.False(Directory.Exists(_root));

            Assert.Equal(0, store.NextSequence);
            Assert.True(store.IsOpened);
            Assert.True(Directory.Exists(Path.Combine(_root, TableBlockDatabase.BlocksName)));
        }

        [Fact]
        public void Blocks_are_stored_in_key_order_and_survive_a_restart()
        {
            var store = Open();

            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, i).Outcome);
                _now = _now.AddMinutes(20);
            }

            var entries = store.GetBlocks(_product);
            Assert.Equal([0L, 1, 2, 3, 4], entries.Select(e => e.Sequence));
            Assert.Equal(TableBlockDatabase.FloorHour(T0), entries[0].ArrivalHourTicks);
            Assert.True(entries[4].ArrivalHourTicks > entries[0].ArrivalHourTicks);
            Assert.Equal(5, store.Statistics.BlocksStored);

            store.Dispose();
            var reopened = Open();

            Assert.Equal(entries, reopened.GetBlocks(_product));

            // A restart continues above the sequence mark, which the store wrote ahead (in steps) before it handed
            // out sequence 0: the sequences in between are skipped, never handed out twice.
            Assert.Equal(TableBlockDatabase.SequenceMarkStep, reopened.NextSequence);
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(reopened, 5).Outcome);
            Assert.Equal(TableBlockDatabase.SequenceMarkStep, reopened.GetBlocks(_product)[^1].Sequence);

            reopened.Dispose();
            AssertKeysGrow();
        }

        [Fact]
        public void A_stored_block_reads_back_unchanged_with_its_envelope()
        {
            var store = Open();
            var block = Block(("a", 3), ("b", 1));
            var refusals = new[] { new TableRefusal(1, TableRefusalReason.UnknownSensor) };

            var result = Append(store, block, producerSequence: 7, refusals);

            var record = store.ReadBlock(result.Entry);
            Assert.Equal(block, record.Block.ToArray());
            Assert.Equal(_product, record.Envelope.ProductId);
            Assert.Equal(_producer, record.Envelope.ProducerId);
            Assert.Equal(7, record.Envelope.ProducerSequence);
            Assert.Equal(T0, record.Envelope.ArrivalUtc);
            Assert.Equal(refusals, record.Envelope.Refusals);
            Assert.True(record.Envelope.IsRefused(1));
            Assert.False(record.Envelope.IsRefused(0));
            Assert.Equal(4, result.Entry.RowCount);
            Assert.Equal(2, result.Entry.TableCount);
            Assert.Equal(1, result.Entry.RefusedCount);
        }

        [Fact]
        public void A_block_whose_producer_sequence_is_stored_is_not_stored_again_also_after_a_restart()
        {
            var store = Open();
            var first = Append(store, Block(("a", 2)), producerSequence: 1, [new TableRefusal(0, TableRefusalReason.SensorBlocked)]);

            var again = Append(store, Block(("a", 2)), producerSequence: 1);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, again.Outcome);
            Assert.Equal(first.Entry, again.Entry);
            Assert.Equal(first.Envelope.Refusals, again.Envelope.Refusals);

            // Another producer may use the same number.
            Assert.Equal(TableBlockAppendOutcome.Stored, Append(store, Block(("a", 2)), producerSequence: 1, producer: Guid.NewGuid()).Outcome);

            store.Dispose();
            var reopened = Open();

            Assert.Equal(TableBlockAppendOutcome.Duplicate, Append(reopened, Block(("a", 2)), producerSequence: 1).Outcome);
            Assert.Equal(2, reopened.BlockCount);
            Assert.Equal(1, reopened.Statistics.DuplicateBlocks);
        }

        [Fact]
        public void Duplicates_are_per_product_so_one_product_cannot_shadow_another()
        {
            // Review of PR #1857: the same producer pair under two products is two blocks. Keyed by producer only, the
            // second was answered Duplicate with the first product's block and never written.
            var store = Open();
            var other = Guid.NewGuid();

            var first = Append(store, Block(("a", 2)), producerSequence: 7);
            var second = Append(store, Block(("b", 3)), producerSequence: 7, product: other);

            Assert.Equal(TableBlockAppendOutcome.Stored, first.Outcome);
            Assert.Equal(TableBlockAppendOutcome.Stored, second.Outcome);
            Assert.Equal(_product, Assert.Single(store.GetBlocks(_product)).ProductId);
            Assert.Equal(other, Assert.Single(store.GetBlocks(other)).ProductId);
            Assert.Equal(other, store.ReadBlock(second.Entry).Envelope.ProductId);

            store.Dispose();
            var reopened = Open();

            var again = Append(reopened, Block(("a", 2)), producerSequence: 7);
            var againOther = Append(reopened, Block(("b", 3)), producerSequence: 7, product: other);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, again.Outcome);
            Assert.Equal(first.Entry.Sequence, again.Entry.Sequence);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, againOther.Outcome);
            Assert.Equal(second.Entry.Sequence, againOther.Entry.Sequence);
        }

        [Fact]
        public void A_reader_during_a_reopen_sees_the_whole_old_index_never_a_cleared_one()
        {
            var store = Open();
            for (int i = 0; i < 4; i++)
                Put(store, i);
            var before = store.GetBlocks(_product).ToList();

            // A failure, the backoff passed, the next use reopens; a reader inside the reopen (before the index is
            // loaded) sees the old index in full.
            store.FailForTest(new IOException("simulated"));
            _now = _now.AddMinutes(5);

            List<TableBlockIndexEntry> seen = null;
            store.OnOpen = () => seen = store.GetBlocks(_product).ToList();

            _ = store.NextSequence;

            Assert.Equal(before, seen);
            Assert.Equal(before, store.GetBlocks(_product));
        }

        [Fact]
        public void A_block_that_is_not_to_be_stored_writes_nothing()
        {
            var store = Open();

            var result = Append(store, Block(("a", 2)), producerSequence: 1, store: false);

            Assert.Equal(TableBlockAppendOutcome.NotStored, result.Outcome);
            Assert.Equal(0, store.BlockCount);
            Assert.Equal(1, store.Statistics.BlocksNotStored);
            Assert.Equal(TableBlockAppendOutcome.Stored, Append(store, Block(("a", 2)), producerSequence: 1).Outcome);
        }

        [Fact]
        public void A_crash_between_the_block_and_its_index_entry_loses_nothing_and_brings_nothing_back()
        {
            var store = Open();
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, 0).Outcome);

            store.FaultHook = point => throw new IOException($"crash at {point}");
            Assert.Throws<IOException>(() => Put(store, 1));
            store.Dispose();

            // The block reached the disk, its index entry did not: the open indexes it again.
            var reopened = Open();
            Assert.Equal([0L, 1], reopened.GetBlocks(_product).Select(e => e.Sequence));

            // The collector was not answered, so it sends the block again: stored once.
            Assert.Equal(TableBlockAppendOutcome.Duplicate, Put(reopened, 1).Outcome);
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(reopened, 2).Outcome);

            reopened.Dispose();
            Assert.Equal([0L, 1, TableBlockDatabase.SequenceMarkStep + 1], Open().GetBlocks(_product).Select(e => e.Sequence));
        }

        [Fact]
        public void A_deleted_index_is_rebuilt_from_the_blocks()
        {
            var store = Open();
            for (int i = 0; i < 4; i++)
                Put(store, i);
            var entries = store.GetBlocks(_product).ToList();
            store.Dispose();

            Directory.Delete(Path.Combine(_root, TableBlockDatabase.IndexName), recursive: true);

            var reopened = Open();
            Assert.Equal(entries, reopened.GetBlocks(_product));
            Assert.Equal(1, reopened.Statistics.IndexRebuilds);
            Assert.Equal(TableBlockAppendOutcome.Duplicate, Put(reopened, 2).Outcome);
        }

        [Fact]
        public void A_damaged_index_entry_makes_the_open_rebuild_the_index()
        {
            var store = Open();
            for (int i = 0; i < 3; i++)
                Put(store, i);
            var entries = store.GetBlocks(_product).ToList();
            store.Dispose();

            using (var index = new DB(Path.Combine(_root, TableBlockDatabase.IndexName), new Options()))
                index.Put(TableBlockDatabase.Key(entries[1].ArrivalHourTicks, entries[1].Sequence), [9, 9, 9]);

            var reopened = Open();
            Assert.Equal(entries, reopened.GetBlocks(_product));
            Assert.Equal(1, reopened.Statistics.IndexRebuilds);
        }

        [Fact]
        public void A_clock_that_steps_back_never_writes_a_smaller_key()
        {
            var store = Open();
            Put(store, 0);

            _now = T0.AddHours(-3);
            var late = Put(store, 1);

            Assert.Equal(TableBlockDatabase.FloorHour(T0), late.Entry.ArrivalHourTicks);
            Assert.Equal(T0.AddHours(-3).Ticks, late.Entry.ArrivalTicks);
            Assert.Equal(T0.AddHours(-3), store.ReadBlock(late.Entry).Envelope.ArrivalUtc);

            _now = T0.AddHours(2);
            Assert.Equal(TableBlockDatabase.FloorHour(T0.AddHours(2)), Put(store, 2).Entry.ArrivalHourTicks);

            store.Dispose();
            AssertKeysGrow();
        }

        [Fact]
        public void Appends_wait_for_earlier_tickets_and_a_skipped_ticket_lets_them_go()
        {
            var store = Open();
            var first = store.Reserve();
            var second = store.Reserve();

            var appended = Task.Run(() => store.Append(second, Envelope(2), Block(("a", 1)), 0, 0, 1, 1));

            Assert.False(appended.Wait(200), "the second ticket was written before the first was done");

            store.Skip(first);

            Assert.True(appended.Wait(5000));
            Assert.Equal(TableBlockAppendOutcome.Stored, appended.Result.Outcome);
            Assert.Equal(second.Sequence, appended.Result.Entry.Sequence);
        }

        [Fact]
        public void Puts_from_many_threads_and_concurrent_reads_keep_the_key_growing()
        {
            var store = Open();
            const int threads = 8, perThread = 40;
            var stop = false;

            var reader = Task.Run(() =>
            {
                var seen = 0;
                while (!Volatile.Read(ref stop))
                {
                    var blocks = store.GetBlocks(_product);
                    Assert.True(blocks.Count >= seen);
                    for (int i = 1; i < blocks.Count; i++)
                        Assert.True(blocks[i].Sequence > blocks[i - 1].Sequence);
                    if (blocks.Count > 0)
                        Assert.NotNull(store.ReadBlock(blocks[^1]));
                    seen = blocks.Count;
                }
            });

            Parallel.For(0, threads, t =>
            {
                for (int i = 0; i < perThread; i++)
                {
                    var ticket = store.Reserve();

                    if (i % 7 == 3)
                        store.Skip(ticket);
                    else
                        store.Append(ticket, Envelope(t * 1000 + i), Block(("a", 1 + i % 5)), 0, 0, 1, 1);
                }
            });

            Volatile.Write(ref stop, true);
            reader.Wait();

            var expected = threads * perThread - threads * Enumerable.Range(0, perThread).Count(i => i % 7 == 3);
            Assert.Equal(expected, store.BlockCount);
            Assert.Equal(threads * perThread, store.NextSequence);

            store.Dispose();
            AssertKeysGrow();
        }

        [Fact]
        public void Puts_are_refused_as_a_fault_while_the_disk_is_short_of_space()
        {
            var store = Open();
            var free = 10L;
            store.FreeBytes = () => free;
            store.MinFreeBytes = 100;

            Assert.Throws<TableBlockStoreException>(() => store.Reserve());
            Assert.Equal(1, store.Statistics.WriteFailures);

            free = 1000;
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, 0).Outcome);
        }

        [Fact]
        public void Ingest_never_makes_LevelDB_compact_either_database()
        {
            // A small write buffer makes LevelDB flush its memtable often. A key that only grows makes every new table
            // file lie above the ones before it: LevelDB places a flushed file that overlaps nothing directly at a lower
            // level, or moves it down without rewriting it ("Moved #n"). A line "Compacting n@l + m@l+1 files" in its
            // LOG is a real compaction, a rewrite of stored bytes, which must never happen during ingest.
            var store = new TableBlockDatabase(_root, writeBufferSize: 16 * 1024) { Clock = () => _now };
            _stores.Add(store);

            var block = Block(("a", 40), ("b", 40), ("c", 40));
            for (int i = 0; i < 5000; i++)
            {
                var ticket = store.Reserve();
                store.Append(ticket, Envelope(i), block, 0, 0, 120, 3);

                if (i % 100 == 99)
                    _now = _now.AddMinutes(30);
            }

            store.Dispose();

            foreach (var database in new[] { TableBlockDatabase.BlocksName, TableBlockDatabase.IndexName })
            {
                var log = ReadLog(Path.Combine(_root, database));

                Assert.True(Regex.Matches(log, @"Level-0 table #\d+: started").Count >= 8, $"{database}: too few memtable flushes to say anything:\n{log}");
                var compactions = Regex.Matches(log, @"Compacting \d+@\d+ \+ \d+@\d+ files");
                Assert.True(compactions.Count == 0, $"{database}: {compactions.Count} real compactions during ingest:\n{log}");
            }
        }


        [Fact]
        public void Deleting_an_arrival_hour_removes_its_blocks_everywhere_and_the_key_keeps_growing()
        {
            // The delete the merge step (#1850) will make; the benchmark measures it.
            var store = Open();
            Put(store, 0);
            Put(store, 1);
            _now = T0.AddHours(1);
            Put(store, 2);

            Assert.Equal(2, store.DeleteArrivalHour(TableBlockDatabase.FloorHour(T0)));

            Assert.Equal([2L], store.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Equal(1, store.BlockCount);

            // The duplicate map forgot the deleted blocks; the sequence still grows.
            Assert.Equal(TableBlockAppendOutcome.Stored, Put(store, 0).Outcome);
            Assert.Equal(3, store.GetBlocks(_product)[^1].Sequence);

            store.Dispose();
            Assert.Equal([2L, 3], Open().GetBlocks(_product).Select(e => e.Sequence));
        }

        [Fact]
        public void Sequences_handed_out_without_a_stored_block_are_never_handed_out_again_after_a_restart()
        {
            // Review of #1844 2b: blocks 0..10 stored, 11..500 consumed by puts that stored nothing, then a table sensor
            // records 501 as its first sequence. A restart used to restore 11 and refuse that sensor's tables.
            var store = Open();
            for (int i = 0; i <= 10; i++)
                Put(store, i);

            for (int i = 11; i <= 500; i++)
            {
                if (i % 2 == 0)
                    Append(store, Block(("a", 1)), producerSequence: 1000 + i, store: false);
                else
                    store.Skip(store.Reserve());
            }

            var firstSequence = store.NextSequence;
            Assert.Equal(501, firstSequence);
            store.Dispose();

            var reopened = Open();
            Assert.True(reopened.NextSequence >= firstSequence);
            Assert.True(Put(reopened, 2000).Entry.Sequence >= firstSequence);
        }

        [Fact]
        public void A_first_sequence_read_before_any_block_is_stored_holds_across_a_restart()
        {
            // A table sensor is registered (it reads NextSequence) and the server restarts before any block arrives.
            var store = Open();
            for (int i = 0; i < 5; i++)
                store.Skip(store.Reserve());

            var firstSequence = store.NextSequence;
            store.Dispose();

            var reopened = Open();
            Assert.Equal(0, reopened.BlockCount);
            Assert.True(reopened.NextSequence >= firstSequence);
            Assert.True(Put(reopened, 1).Entry.Sequence >= firstSequence);
        }

        [Fact]
        public void The_mark_survives_an_index_rebuild_and_a_block_database_emptied_by_deletes()
        {
            var store = Open();
            Put(store, 0);
            for (int i = 0; i < 100; i++)
                store.Skip(store.Reserve());
            var firstSequence = store.NextSequence;

            // Every block deleted (what #1850 does to merged hours): the database is empty, the mark is not.
            store.DeleteArrivalHour(TableBlockDatabase.FloorHour(T0));
            store.Dispose();

            Directory.Delete(Path.Combine(_root, TableBlockDatabase.IndexName), recursive: true);

            var reopened = Open();
            Assert.Equal(0, reopened.BlockCount);
            Assert.True(reopened.NextSequence >= firstSequence);

            Put(reopened, 1);
            reopened.Dispose();
            Directory.Delete(Path.Combine(_root, TableBlockDatabase.IndexName), recursive: true);

            var rebuilt = Open();
            Assert.True(rebuilt.NextSequence > firstSequence);
            Assert.Equal(1, rebuilt.Statistics.IndexRebuilds);
        }

        [Fact]
        public void Raising_the_next_sequence_is_durable_and_waits_for_tickets_in_flight()
        {
            var store = Open();
            var inFlight = store.Reserve();

            store.RaiseNextSequence(5000);
            Assert.Equal(5000, store.NextSequence);

            // The ticket reserved before the raise is written first; the one after it waits for it, then takes 5000.
            var after = store.Reserve();
            Assert.Equal(5000, after.Sequence);
            var appended = Task.Run(() => store.Append(after, Envelope(2), Block(("a", 1)), 0, 0, 1, 1));
            Assert.False(appended.Wait(200));

            store.Skip(inFlight);
            Assert.True(appended.Wait(5000));
            Assert.Equal(5000, appended.Result.Entry.Sequence);

            // A lower floor changes nothing; the raise survives a restart.
            store.RaiseNextSequence(10);
            Assert.Equal(5001, store.NextSequence);
            store.Dispose();

            Assert.True(Open().NextSequence >= 5001);
        }

        [Fact]
        public void A_ticket_done_at_the_far_end_of_a_raised_gap_does_not_stop_later_appends()
        {
            // Review of #1844 2b: ticket 5 in flight, the next sequence raised to 100, ticket 100 reserved and skipped
            // before ticket 5 is done, then ticket 5 done. The commit order must jump over the gap and drain ticket 100,
            // or the append of ticket 101 waits for good.
            var store = Open();
            for (int i = 0; i < 5; i++)
                store.Skip(store.Reserve());

            var inFlight = store.Reserve();
            Assert.Equal(5, inFlight.Sequence);

            store.RaiseNextSequence(100);

            var farEnd = store.Reserve();
            Assert.Equal(100, farEnd.Sequence);
            store.Skip(farEnd);

            var next = store.Reserve();
            Assert.Equal(101, next.Sequence);
            var appended = Task.Run(() => store.Append(next, Envelope(101), Block(("a", 1)), 0, 0, 1, 1));

            store.Skip(inFlight);

            // A generous bound that only matters when the order is stuck: the test fails instead of hanging.
            Assert.True(appended.Wait(TimeSpan.FromSeconds(30)), "the append of ticket 101 never got its turn");
            Assert.Equal(TableBlockAppendOutcome.Stored, appended.Result.Outcome);
            Assert.Equal(101, appended.Result.Entry.Sequence);
        }

        [Fact]
        public void Two_raises_with_a_ticket_in_flight_do_not_stop_later_appends()
        {
            // Review of #1844 2b: ticket 5 in flight, raises to 100 and to 200 with nothing reserved between them. The
            // second raise must extend the first gap, not replace it, or ticket 5 lands on 6 with no gap there.
            var store = Open();
            for (int i = 0; i < 5; i++)
                store.Skip(store.Reserve());

            var inFlight = store.Reserve();
            store.RaiseNextSequence(100);
            store.RaiseNextSequence(200);

            var next = store.Reserve();
            Assert.Equal(200, next.Sequence);
            var appended = Task.Run(() => store.Append(next, Envelope(200), Block(("a", 1)), 0, 0, 1, 1));

            store.Skip(inFlight);

            Assert.True(appended.Wait(TimeSpan.FromSeconds(30)), "the append of ticket 200 never got its turn");
            Assert.Equal(200, appended.Result.Entry.Sequence);
        }

        [Fact]
        public void Two_raises_with_a_reservation_between_them_and_tickets_in_flight_do_not_stop_later_appends()
        {
            // Ticket 5 in flight; raise to 100; ticket 100 reserved and still in flight; raise to 200: two gaps, (6, 100)
            // and (101, 200). Ticket 100 is done before ticket 5.
            var store = Open();
            for (int i = 0; i < 5; i++)
                store.Skip(store.Reserve());

            var first = store.Reserve();
            store.RaiseNextSequence(100);
            var second = store.Reserve();
            Assert.Equal(100, second.Sequence);
            store.RaiseNextSequence(200);

            var next = store.Reserve();
            Assert.Equal(200, next.Sequence);
            var appended = Task.Run(() => store.Append(next, Envelope(200), Block(("a", 1)), 0, 0, 1, 1));

            store.Skip(second);
            store.Skip(first);

            Assert.True(appended.Wait(TimeSpan.FromSeconds(30)), "the append of ticket 200 never got its turn");
            Assert.Equal(200, appended.Result.Entry.Sequence);
        }

        [Fact]
        public void The_mark_is_written_ahead_in_steps()
        {
            var store = Open();
            var mark = Path.Combine(_root, TableBlockDatabase.SequenceMarkName);

            store.Skip(store.Reserve());
            Assert.Equal(TableBlockDatabase.SequenceMarkStep.ToString(), File.ReadAllText(mark));

            var written = File.GetLastWriteTimeUtc(mark);
            for (int i = 1; i < TableBlockDatabase.SequenceMarkStep; i++)
                store.Skip(store.Reserve());
            Assert.Equal(TableBlockDatabase.SequenceMarkStep.ToString(), File.ReadAllText(mark));

            store.Skip(store.Reserve());
            Assert.Equal((2 * TableBlockDatabase.SequenceMarkStep).ToString(), File.ReadAllText(mark));
        }

        [Fact]
        public void A_damaged_mark_fails_the_open_as_a_fault_that_names_the_file()
        {
            var store = Open();
            Put(store, 0);
            store.Dispose();

            File.WriteAllText(Path.Combine(_root, TableBlockDatabase.SequenceMarkName), "twelve");

            var reopened = Open();
            var error = Assert.Throws<TableBlockStoreException>(() => reopened.Reserve());
            Assert.Contains(TableBlockDatabase.SequenceMarkName, error.Message);
        }

        [Fact]
        public void An_envelope_records_more_refused_tables_than_sixteen_bits_count()
        {
            var refusals = Enumerable.Range(0, 70_000).Select(i => new TableRefusal(i, TableRefusalReason.UnknownSensor)).ToArray();
            var store = Open();

            var stored = Append(store, Block(("a", 1)), producerSequence: 1, refusals);

            Assert.Equal(70_000, stored.Entry.RefusedCount);
            Assert.Equal(refusals, store.ReadBlock(stored.Entry).Envelope.Refusals);

            store.Dispose();
            Assert.Equal(70_000, Open().GetBlocks(_product)[0].RefusedCount);
        }

        [Fact]
        public void The_compaction_check_sees_a_real_compaction_when_keys_do_not_grow()
        {
            // The detector of the test above, proved on the case it must catch: keys in random order.
            var path = Path.Combine(_root, "random");
            Directory.CreateDirectory(_root);
            var random = new Random(1844);
            var value = new byte[1024];

            using (var db = new DB(path, new Options { CreateIfMissing = true, WriteBufferSize = 16 * 1024, CompressionLevel = CompressionLevel.NoCompression }))
            {
                for (int i = 0; i < 5000; i++)
                {
                    random.NextBytes(value);
                    db.Put(BitConverter.GetBytes(random.NextInt64()), value);
                }
            }

            Assert.True(Regex.Matches(ReadLog(path), @"Compacting \d+@\d+ \+ \d+@\d+ files").Count > 0);
        }


        // === Helpers ===

        private TableBlockDatabase Open()
        {
            var store = new TableBlockDatabase(_root) { Clock = () => _now };
            _stores.Add(store);
            return store;
        }

        private TableBlockAppendResult Put(TableBlockDatabase store, long producerSequence) =>
            Append(store, Block(("a", 2), ("b", 3)), producerSequence);

        private TableBlockAppendResult Append(TableBlockDatabase db, byte[] block, long producerSequence,
            TableRefusal[] refusals = null, Guid? producer = null, bool store = true, Guid? product = null)
        {
            var header = TableBlockReader.ReadHeader(block);
            var ticket = db.Reserve();

            try
            {
                return db.Append(ticket, Envelope(producerSequence, refusals, producer, product), block, header.FirstTime, header.LastTime,
                    header.RowCount, header.TableCount, store);
            }
            finally
            {
                if (!ticket.IsDone)
                    db.Skip(ticket);
            }
        }

        private TableBlockEnvelope Envelope(long producerSequence, TableRefusal[] refusals = null, Guid? producer = null, Guid? product = null) => new()
        {
            ProductId = product ?? _product,
            ProducerId = producer ?? _producer,
            ProducerSequence = producerSequence,
            Refusals = refusals ?? [],
        };

        internal static byte[] Block(params (string Id, int Rows)[] tables)
        {
            using var writer = new TableBlockWriter();

            foreach (var (id, rows) in tables)
            {
                var time = Enumerable.Range(0, rows).Select(i => T0.Ticks + i * TimeSpan.TicksPerMillisecond).ToArray();
                var value = Enumerable.Range(0, rows).Select(i => 100.0 + i * 0.25).ToArray();

                writer.BeginTable(Encoding.UTF8.GetBytes(id), 1, rows);
                writer.AddTimeColumn(time);
                writer.AddDoubleColumn(value);
            }

            return writer.ToArray();
        }

        private void AssertKeysGrow()
        {
            foreach (var name in new[] { TableBlockDatabase.BlocksName, TableBlockDatabase.IndexName })
            {
                using var db = new DB(Path.Combine(_root, name), new Options());
                using var iterator = db.CreateIterator();

                byte[] previous = null;
                long previousSequence = -1, previousHour = 0;

                for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
                {
                    var key = iterator.Key();
                    var (hour, sequence) = TableBlockDatabase.ParseKey(key);

                    if (previous is not null)
                    {
                        Assert.True(key.AsSpan().SequenceCompareTo(previous) > 0);
                        Assert.True(sequence > previousSequence);
                        Assert.True(hour >= previousHour);
                    }

                    previous = key;
                    previousSequence = sequence;
                    previousHour = hour;
                }
            }
        }

        private static string ReadLog(string folder)
        {
            var text = new StringBuilder();

            foreach (var file in Directory.GetFiles(folder, "LOG*"))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text.AppendLine(reader.ReadToEnd());
            }

            return text.ToString();
        }
    }
}
