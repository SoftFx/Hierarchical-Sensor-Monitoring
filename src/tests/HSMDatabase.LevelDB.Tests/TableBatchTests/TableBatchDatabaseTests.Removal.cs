using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.TableBatchTests
{
    // The store's part of the removal of table sensors and root products (#1850 part 4d): the record of removals (durable,
    // never written over unread, cleared mark by mark) and the removal of a product's folder whole (test 7 of the design).
    public sealed partial class TableBatchDatabaseTests
    {
        // === The record of removals ===

        [Fact]
        public void The_record_of_removals_survives_a_restart_as_marks_of_an_earlier_run_and_clears_only_the_same_mark()
        {
            var (s1, s2, root) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var store = Open();

            store.MarkRemoved([(_product, s1), (Guid.Empty, s2)], [root]);
            var expected = new List<TableRemoval> { new(root, null, false) };
            expected.AddRange(new[] { new TableRemoval(_product, s1, false), new TableRemoval(Guid.Empty, s2, false) }.OrderBy(m => m.SensorId));
            Assert.Equal(expected, store.GetRemovals());

            // Confirmed in memory only: Ready from then on; an id without a mark is skipped.
            store.ConfirmRemoved([s1, Guid.NewGuid()], []);
            Assert.Equal([true, false, false], new[] { store.GetRemovals().Single(m => m.SensorId == s1), store.GetRemovals().Single(m => m.SensorId == s2),
                store.GetRemovals().Single(m => m.SensorId is null) }.Select(m => m.Ready));
            store.Dispose();

            // Read back as marks of an earlier run.
            store = Open();
            var earlier = store.GetRemovals();
            Assert.All(earlier, m => Assert.True(m.Earlier));
            Assert.Equal(3, earlier.Count);

            // Marked again in this run: a sensor whose product the call does not know keeps the product named before; one
            // whose product was not known gets it.
            store.MarkRemoved([(Guid.Empty, s1), (_product, s2)], []);
            Assert.Contains(new TableRemoval(_product, s1, false), store.GetRemovals());
            Assert.Contains(new TableRemoval(_product, s2, false), store.GetRemovals());

            // Clearing the marks of the earlier run read before: the sensors marked again in this run keep their new marks.
            store.ClearRemovals(earlier);
            Assert.Equal(new[] { new TableRemoval(_product, s1, false), new TableRemoval(_product, s2, false) }.OrderBy(m => m.SensorId), store.GetRemovals().OrderBy(m => m.SensorId));
            store.Dispose();

            store = Open();
            Assert.Equal(new[] { new TableRemoval(_product, s1, true), new TableRemoval(_product, s2, true) }.OrderBy(m => m.SensorId), store.GetRemovals().OrderBy(m => m.SensorId));

            // Emptied: the file goes.
            store.ClearRemovals(store.GetRemovals());
            Assert.Empty(store.GetRemovals());
            Assert.False(File.Exists(RemovedPath()));
        }

        [Fact]
        public void A_removal_is_found_by_its_sensor_or_its_product_as_it_is_now()
        {
            var (sensor, root) = (Guid.NewGuid(), Guid.NewGuid());
            var store = Open();

            Assert.Null(store.FindRemoval(new TableRemoval(_product, sensor, false)));

            store.MarkRemoved([(_product, sensor)], [root]);
            var mark = new TableRemoval(_product, sensor, false);
            Assert.Equal(mark, store.FindRemoval(mark));
            Assert.Equal(new TableRemoval(root, null, false), store.FindRemoval(new TableRemoval(root, null, true)));

            // The mark as it is now: confirmed since, it is no longer the mark read before.
            store.ConfirmRemoved([sensor], []);
            Assert.NotEqual(mark, store.FindRemoval(mark));
            Assert.Equal(mark with { Confirmed = true }, store.FindRemoval(mark));

            store.ClearRemovals([mark with { Confirmed = true }]);
            Assert.Null(store.FindRemoval(mark));
            store.Dispose();
        }

        [Fact]
        public void A_record_of_removals_that_cannot_be_read_is_a_fault_at_every_call_until_it_reads_and_is_never_written_over()
        {
            var sensor = Guid.NewGuid();
            var store = Open();
            store.MarkRemoved([(_product, sensor)], []);
            store.Dispose();

            // A line that is not a removal is skipped (logged), the others read.
            File.AppendAllText(RemovedPath(), "X not a removal\n");

            // Nothing is taken as removed while the record is not known, and nothing is written over it.
            store = Open();
            store.ReadRemovedLines = path => throw new IOException("the file is held by another process");
            Assert.Throws<TableBatchStoreException>(() => store.GetRemovals());
            Assert.Throws<TableBatchStoreException>(() => store.MarkRemoved([(_product, Guid.NewGuid())], []));
            Assert.Throws<TableBatchStoreException>(() => store.GetRemovals());
            Assert.Contains($"S {sensor:N} {_product:N}", File.ReadAllText(RemovedPath()));
            store.ReadRemovedLines = File.ReadAllLines;

            Assert.Equal([new TableRemoval(_product, sensor, true)], store.GetRemovals());

            var other = Guid.NewGuid();
            store.MarkRemoved([(_product, other)], []);
            Assert.Equal(2, store.GetRemovals().Count);
            Assert.DoesNotContain("X not a removal", File.ReadAllText(RemovedPath()));
        }

        [Fact]
        public void A_record_of_removals_whose_write_fails_changes_nothing()
        {
            var store = Open();
            var sensor = Guid.NewGuid();
            store.MarkRemoved([(_product, sensor)], []);

            store.Checkpoint = p => { if (p == "removals-writing") throw new IOException("disk full"); };
            Assert.Throws<TableBatchStoreException>(() => store.MarkRemoved([(_product, Guid.NewGuid())], []));
            Assert.Throws<TableBatchStoreException>(() => store.ClearRemovals(store.GetRemovals()));
            store.Checkpoint = null;

            Assert.Equal([new TableRemoval(_product, sensor, false)], store.GetRemovals());
            store.Dispose();
            Assert.Equal([new TableRemoval(_product, sensor, true)], Open().GetRemovals());
        }


        // === RemoveProduct (design test 7) ===

        [Fact]
        public void A_removed_product_reads_as_absent_refuses_writes_and_its_folder_is_renamed_and_deleted_while_another_product_stays()
        {
            var other = Guid.NewGuid();
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());
            store.WriteHour(_product, Commit(Week0 + Week), TwoParts());
            store.WriteHour(other, Commit(Week0), TwoParts());
            var otherBatches = store.ReadSensor(other, _sensorA, 1, [Week0]);
            var folder = Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"));
            Assert.True(store.OpenDatabases > 0);

            store.RemoveProduct(_product);

            Assert.False(Directory.Exists(folder));
            Assert.Empty(Directory.GetDirectories(Path.Combine(_root, TableBatchDatabase.FolderName), $"{_product:N}*"));
            Assert.Equal([other], store.GetProducts());

            // Reads find nothing, as for a product without a folder; writes are refused and create nothing.
            Assert.Empty(store.GetHours(_product));
            Assert.Empty(store.GetWeeks(_product));
            Assert.Null(store.GetPlacement(_product, _sensorA));
            Assert.Empty(store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.Equal(0, store.GetGeneration(_product));
            Assert.Throws<TableBatchStoreException>(() => store.WriteHour(_product, Commit(Week0 + 2 * Week), TwoParts()));
            Assert.Throws<TableBatchStoreException>(() => store.SetPlacement(_product, _sensorA, new TablePlacement(1, Week0)));
            Assert.False(Directory.Exists(folder));

            // Another product is as it was; a repeat of the removal does nothing.
            Assert.Equal(otherBatches.Select(b => b.Batch), store.ReadSensor(other, _sensorA, 1, [Week0]).Select(b => b.Batch));
            store.RemoveProduct(_product);
            Assert.Equal([other], store.GetProducts());

            // The next run: the product is absent, nothing of it comes back.
            store.Dispose();
            store = Open();
            Assert.Equal([other], store.GetProducts());
            Assert.Empty(store.GetHours(_product));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_removed_products_folder_left_by_a_crash_or_a_failed_delete_is_never_listed_and_the_next_run_deletes_it(bool crash)
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());
            var products = Path.Combine(_root, TableBatchDatabase.FolderName);

            if (crash)
            {
                store.Checkpoint = p => { if (p == "product-renamed") throw new CheckpointException(p); };
                Assert.Throws<CheckpointException>(() => store.RemoveProduct(_product));
                store.CloseWithoutFlush();
            }
            else
            {
                store.DeleteFolder = path => throw new IOException("a file is held open");
                store.RemoveProduct(_product);
                Assert.Empty(store.GetProducts());
                store.Dispose();
            }

            var left = Assert.Single(Directory.GetDirectories(products));
            Assert.StartsWith(_product.ToString("N") + TableBatchDatabase.RemovedFolderSuffix, Path.GetFileName(left));

            store = Open();
            Assert.Empty(store.GetProducts());
            Assert.Empty(Directory.GetDirectories(products));
            Assert.Empty(store.GetHours(_product));
        }

        [Fact]
        public void A_product_removal_waits_for_a_read_that_holds_one_of_its_databases_and_the_read_completes()
        {
            var store = Open();
            store.WriteHour(_product, Commit(Week0), TwoParts());

            using var holding = new ManualResetEventSlim();
            using var removeWaiting = new ManualResetEventSlim();
            var gate = new ManualResetEventSlim(); // not disposed: a thread of a failed test may still wait on it
            store.Checkpoint = p =>
            {
                if (p == "read-holding")
                {
                    holding.Set();
                    gate.Wait();
                }
                else if (p == "product-remove-waiting")
                    removeWaiting.Set();
            };

            var read = StartLong(() => store.ReadSensor(_product, _sensorA, 1, [Week0]));
            Assert.True(holding.Wait(Bound), "the read holds the week database");
            var remove = StartLong(() => store.RemoveProduct(_product));
            Assert.True(removeWaiting.Wait(Bound), "the removal waits for the read");
            gate.Set();

            Assert.True(read.Wait(Bound));
            Assert.Equal(2, read.Result.Count);
            Assert.True(remove.Wait(Bound));
            store.Checkpoint = null;

            Assert.False(Directory.Exists(Path.Combine(_root, TableBatchDatabase.FolderName, _product.ToString("N"))));
            Assert.Equal(0, store.OpenDatabases);
        }

        private string RemovedPath() => Path.Combine(_root, TableBatchDatabase.FolderName, TableBatchDatabase.RemovedName);
    }
}
