using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1803: what the sealed week store does besides answering like a row database: unreadable chunks, the
// sealed-and-complete signal, File values, readers concurrent with a chunk rewrite, the compaction marker.
public sealed class SealedWeekStoreTests
{
    private static byte[] Key(Guid sensor, long tick) => new DbKey(sensor, tick).ToBytes();

    private static byte[] Min(Guid sensor) => Key(sensor, 0);

    private static byte[] Max(Guid sensor) => Key(sensor, DateTime.MaxValue.Ticks);

    private static List<SealedChunk> DenseChunks(WeekPair pair, Guid dense) =>
        pair.Seal.Chunks.Where(c => c.SensorId == dense).OrderBy(c => c.FirstTick).ToList();


    #region Unreadable chunks

    [Fact]
    public void CorruptChunk_FailsTheReadsThatReachIt_CountsIt_AndNeverShortensThem()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(7));
        SealedChunk corrupt = default;

        using var pair = WeekPair.Create(seed, beforeOpen: p => corrupt = CorruptChunk(p, DenseChunks(p, sensors.Dense)[1]));

        var chunks = DenseChunks(pair, sensors.Dense);
        var store = pair.SealedStore;
        var globalBefore = SealedHistoryDiagnostics.ChunkReadFailures;

        // A read over the whole sensor fails; it does not return the other chunks' rows as if they were all.
        Assert.Throws<SealedChunkReadException>(() => store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)).ToList());
        Assert.Throws<SealedChunkReadException>(() => store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList());
        Assert.Throws<SealedChunkReadException>(() => store.GetAll().ToList());
        Assert.Throws<SealedChunkReadException>(() => store.CalculateHistorySize(Min(sensors.Dense), Max(sensors.Dense)));
        Assert.Equal(4, store.ChunkReadFailures);
        Assert.True(SealedHistoryDiagnostics.ChunkReadFailures >= globalBefore + 4);

        // The newest page lies in the last chunk and reads; going on into the corrupt chunk throws.
        var newestFirst = store.GetValuesTo(Min(sensors.Dense), Max(sensors.Dense)).GetEnumerator();
        for (int i = 0; i < chunks[2].Rows; i++)
            Assert.True(newestFirst.MoveNext());
        Assert.Throws<SealedChunkReadException>(() => newestFirst.MoveNext());
        newestFirst.Dispose();

        // Ranges that do not reach it read like the row database.
        pair.Compare.Values(pair.Plain.GetValuesFrom(Key(sensors.Dense, chunks[0].FirstTick), Key(sensors.Dense, chunks[0].LastTick)),
            store.GetValuesFrom(Key(sensors.Dense, chunks[0].FirstTick), Key(sensors.Dense, chunks[0].LastTick)), "first chunk");

        // A point read inside it behaves like a failed point read of the row database: logged, counted, no value.
        var failures = store.ChunkReadFailures;
        Assert.Null(store.GetLatest(Key(sensors.Dense, corrupt.LastTick), Key(sensors.Dense, 0).AsSpan(0, 16).ToArray()));
        Assert.Equal(failures + 1, store.ChunkReadFailures);
    }

    [Fact]
    public void CorruptChunk_OnTheBorderOfARangeDelete_RefusesTheWholeDelete()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(8));
        SealedChunk corrupt = default;

        using var pair = WeekPair.Create(seed, beforeOpen: p => corrupt = CorruptChunk(p, DenseChunks(p, sensors.Dense)[1]));

        var store = pair.SealedStore;
        var overlayKey = Key(sensors.Dense, corrupt.FirstTick + 1);
        var overlayValue = TestValues.Bytes(SensorType.Double, corrupt.FirstTick + 1, new Random(1));
        store.PutSensorValue(overlayKey, overlayValue);

        var refusedBefore = SealedHistoryDiagnostics.ChunkRewriteFailures;
        var changes = store.ChangeCount;

        // From inside the first chunk to inside the corrupt one: the corrupt chunk would have to be re-encoded.
        var first = DenseChunks(pair, sensors.Dense)[0];
        store.RemoveSensorValues(Key(sensors.Dense, first.FirstTick + 1), Key(sensors.Dense, corrupt.FirstTick + 10 * TimeSpan.TicksPerSecond));

        Assert.True(SealedHistoryDiagnostics.ChunkRewriteFailures > refusedBefore);
        Assert.Equal(changes, store.ChangeCount);
        Assert.Equal(first.Rows, store.GetValuesFrom(Key(sensors.Dense, first.FirstTick), Key(sensors.Dense, first.LastTick)).Count());

        // On disk: every chunk as sealed, and the overlay row in the range still there.
        pair.CloseSealed();

        using (var chunks = new LevelDBDatabaseAdapter(pair.SealedChunkPath))
            Assert.Equal(pair.Seal.Chunks.Count, chunks.GetAll().Count());

        using (var overlay = new LevelDBDatabaseAdapter(pair.SealedRowPath))
        {
            Assert.True(overlay.TryRead(overlayKey, out var stored));
            Assert.Equal(overlayValue, stored);
        }
    }

    [Fact]
    public void CorruptChunk_WhollyInsideARangeDelete_IsDeletedWithIt()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(9));

        using var pair = WeekPair.Create(seed, beforeOpen: p => CorruptChunk(p, DenseChunks(p, sensors.Dense)[1]));

        var chunks = DenseChunks(pair, sensors.Dense);

        // Bounded by the next chunk's first key, which lies inside the range.
        pair.Remove(Key(sensors.Dense, chunks[0].LastTick), Key(sensors.Dense, chunks[2].FirstTick));

        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)),
            pair.Sealed.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)), "after deleting the corrupt chunk");
        pair.AssertSameReads(sensors.All, new Random(9), probesPerSensor: 3);
    }

    [Fact]
    public void CorruptLastChunk_IsDeletedByAWholeSensorDelete()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(10));

        using var pair = WeekPair.Create(seed, beforeOpen: p => CorruptChunk(p, DenseChunks(p, sensors.Dense)[2]));

        pair.Remove(Min(sensors.Dense), Max(sensors.Dense));

        Assert.Empty(pair.Sealed.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)));
        pair.AssertSameReads(sensors.All, new Random(10), probesPerSensor: 3);
    }

    [Fact]
    public void CorruptLastChunk_IsDeletedByARetentionDeleteThatReachesTheEndOfTheWeek()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(19));
        SealedChunk corrupt = default;

        using var pair = WeekPair.Create(seed, beforeOpen: p => corrupt = CorruptChunk(p, DenseChunks(p, sensors.Dense)[2]));

        var store = pair.SealedStore;

        // A cutoff inside the week, past every row of the chunk: its rows cannot be proven inside without reading it.
        var refused = SealedHistoryDiagnostics.ChunkRewriteFailures;
        store.RemoveSensorValues(Min(sensors.Dense), Key(sensors.Dense, corrupt.LastTick + 1));
        Assert.Equal(refused + 1, SealedHistoryDiagnostics.ChunkRewriteFailures);

        // A cutoff at the end of the week (retention of an older week passes a later one): every row of the week is before it.
        pair.Remove(Min(sensors.Dense), Key(sensors.Dense, WeekPair.WeekTo));

        Assert.Equal(refused + 1, SealedHistoryDiagnostics.ChunkRewriteFailures);
        Assert.Empty(store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)));
        pair.AssertSameReads(sensors.All, new Random(19), probesPerSensor: 3);
    }

    [Fact]
    public void CorruptLastChunkOfOneSensor_DoesNotReachTheNextSensor()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(18));
        Guid a = default, b = default;

        using var pair = WeekPair.Create(seed, beforeOpen: p =>
        {
            // A sensor whose last chunk is directly followed by another sensor's chunk.
            var ordered = p.Seal.Chunks.OrderBy(c => Key(c.SensorId, c.FirstTick), ByteOrder.Instance).ToList();
            var i = ordered.FindIndex(c => c.SensorId == sensors.Dense && c.FirstTick == DenseChunks(p, sensors.Dense)[^1].FirstTick);
            if (i == ordered.Count - 1)
                i = ordered.FindLastIndex(ordered.Count - 2, c => c.SensorId != ordered[^1].SensorId);
            a = ordered[i].SensorId;
            b = ordered[i + 1].SensorId;
            CorruptChunk(p, ordered.Where(c => c.SensorId == a).OrderBy(c => c.FirstTick).Last());
        });

        Assert.NotEqual(a, b);
        var store = pair.SealedStore;
        var prefix = Key(b, 0).AsSpan(0, 16).ToArray();

        // Reads of the next sensor, both ways and by point, never touch the corrupt chunk.
        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(b), Max(b)), store.GetKeysValuesTo(Min(b), Max(b)), "next sensor, newest first");
        pair.Compare.Values(pair.Plain.GetValuesFrom(Min(b), Max(b)), store.GetValuesFrom(Min(b), Max(b)), "next sensor, oldest first");
        foreach (var key in new[] { Min(b), Max(b), Key(b, WeekPair.WeekFrom) })
        {
            pair.Compare.Value(pair.Plain.Get(key, prefix), store.Get(key, prefix), "Get");
            pair.Compare.Value(pair.Plain.GetFirst(key, prefix), store.GetFirst(key, prefix), "GetFirst");
            pair.Compare.Value(pair.Plain.GetLatest(key, prefix), store.GetLatest(key, prefix), "GetLatest");
        }

        Assert.Equal(0, store.ChunkReadFailures);

        // A retention-style delete of the next sensor from its minimum goes through.
        var refused = SealedHistoryDiagnostics.ChunkRewriteFailures;
        var ticks = store.GetKeysValuesTo(Min(b), Max(b)).Select(r => DbKey.FromBytes(r.key).Timestamp).ToList();
        pair.Remove(Min(b), Key(b, ticks[ticks.Count / 2]));

        Assert.Equal(refused, SealedHistoryDiagnostics.ChunkRewriteFailures);
        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(b), Max(b)), store.GetKeysValuesTo(Min(b), Max(b)), "next sensor after the delete");
        Assert.Equal(0, store.ChunkReadFailures);
    }

    // Flips one payload byte of a chunk in the closed chunk database.
    private static SealedChunk CorruptChunk(WeekPair pair, SealedChunk chunk)
    {
        using var chunks = new LevelDBDatabaseAdapter(pair.SealedChunkPath);
        var key = Key(chunk.SensorId, chunk.FirstTick);

        Assert.True(chunks.TryRead(key, out var bytes));
        bytes[bytes.Length / 2] ^= 0x5A;
        chunks.Put(key, bytes);

        return chunk;
    }

    #endregion

    #region The sealed-and-complete signal

    [Fact]
    public void SealedDirectoryWithoutTheMarker_IsNotASealedWeek()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hsm-1803-{Guid.NewGuid():N}");
        var rowPath = Path.Combine(root, "SensorValuesV2_1_2");
        var sealedPath = Path.Combine(root, $"{SealedWeekMarker.DirectoryPrefix}_1_2");

        try
        {
            var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(11));

            using (var rows = new LevelDBDatabaseAdapter(rowPath))
                foreach (var (key, value) in seed)
                    rows.Put(key, value);

            // A seal that wrote every chunk and crashed before its marker; a leftover temporary marker too.
            var seal = WeekSealer.Seal(rowPath, sealedPath, skipMarker: "crash");
            File.WriteAllText(Path.Combine(sealedPath, SealedWeekMarker.FileName + ".tmp"), "partial");

            Assert.NotEmpty(seal.Chunks);
            Assert.False(SealedWeekMarker.ExistsIn(sealedPath));

            using var db = LevelDBManager.GetSensorValuesDatabaseInstance(rowPath, sealedPath, 1, 2);

            Assert.IsType<SensorValuesDatabaseWorker>(db);
            Assert.Equal(seed.Count, db.GetAll().Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writer_RefusesAWeekThatIsAlreadySealed()
    {
        var (seed, _) = SealedWeekDifferentialTests.Seed(new Random(12));

        using var pair = WeekPair.Create(seed);

        Assert.True(SealedWeekMarker.ExistsIn(pair.SealedChunkPath));
        Assert.Throws<InvalidOperationException>(() => SealedWeekWriter.Open(pair.SealedChunkPath));
    }

    #endregion

    #region File values

    [Fact]
    public void FileValues_StayRowsOfTheSealedWeek_AndReadBackByteForByte()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(13));

        using var pair = WeekPair.Create(seed);

        Assert.DoesNotContain(pair.Seal.Chunks, c => c.SensorId == sensors.File);

        var expected = seed.Where(r => DbKey.FromBytes(r.key).SensorId == sensors.File).ToList();
        var read = pair.Sealed.GetKeysValuesTo(Min(sensors.File), Max(sensors.File)).Reverse().ToList();

        Assert.Equal(40, expected.Count);
        Assert.Equal(expected.Select(r => r.key), read.Select(r => r.key));
        Assert.Equal(expected.Select(r => r.value), read.Select(r => r.value));

        var latest = expected[^1];
        Assert.Equal(latest.value, pair.Sealed.GetLatest(Max(sensors.File), latest.key.AsSpan(0, 16).ToArray()));
    }

    #endregion

    #region Readers concurrent with a chunk rewrite

    [Fact]
    public void ReaderStartedBeforeARangeDelete_SeesTheWeekAsItWasBefore()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(14));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var edited = Key(sensors.Dense, chunks[1].FirstTick);
        store.PutSensorValue(edited, TestValues.Bytes(SensorType.Double, chunks[1].FirstTick, new Random(2)));

        var before = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();

        // Newest first: the reader is in the last chunk when the delete rewrites the first two.
        using var reader = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).GetEnumerator();
        var seen = new List<(byte[] key, byte[] value)>();
        for (int i = 0; i < 10; i++)
        {
            Assert.True(reader.MoveNext());
            seen.Add(reader.Current);
        }

        store.RemoveSensorValues(Key(sensors.Dense, chunks[0].LastTick - 10 * TimeSpan.TicksPerSecond), Key(sensors.Dense, chunks[1].LastTick - 10 * TimeSpan.TicksPerSecond));

        while (reader.MoveNext())
            seen.Add(reader.Current);

        Assert.Equal(before.Select(r => r.key), seen.Select(r => r.key));
        Assert.Equal(before.Select(r => r.value), seen.Select(r => r.value));
        Assert.True(store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).Count() < before.Count);
    }

    [Fact]
    public void ReaderBetweenTheChunkRewriteAndTheOverlayDelete_SeesOnlyRowsTheWeekHeldBefore()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(15));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var from = Key(sensors.Dense, chunks[0].LastTick - 10 * TimeSpan.TicksPerSecond);
        var to = Key(sensors.Dense, chunks[1].FirstTick + 10 * TimeSpan.TicksPerSecond);

        // In the range: a sealed row hidden by an overlay edit, and a late value only in the overlay.
        var edited = Key(sensors.Dense, chunks[1].FirstTick);
        var late = Key(sensors.Dense, chunks[1].FirstTick + 1);
        pair.Put(edited, TestValues.Bytes(SensorType.Double, chunks[1].FirstTick, new Random(3)));
        pair.Put(late, TestValues.Bytes(SensorType.Double, chunks[1].FirstTick + 1, new Random(4)));

        var before = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToDictionary(r => Convert.ToHexString(r.key), r => r.value);
        List<(byte[] key, byte[] value)> newestFirst = null;
        List<byte[]> oldestFirst = null;

        store.AfterChunkRewrite = () =>
        {
            newestFirst = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();
            oldestFirst = store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)).ToList();
        };

        pair.Remove(from, to);
        store.AfterChunkRewrite = null;

        Assert.NotNull(newestFirst);

        // No duplicates, key order, every row exactly as before.
        var keys = newestFirst.Select(r => Convert.ToHexString(r.key)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(newestFirst.Select(r => r.value).Reverse(), oldestFirst);
        foreach (var (key, value) in newestFirst)
            Assert.Equal(before[Convert.ToHexString(key)], value);

        // Rows outside the range all there; in the range, only the overlay rows the delete has not reached yet.
        bool InRange(string hex) => ByteOrder.Instance.Compare(Convert.FromHexString(hex), from) >= 0 && ByteOrder.Instance.Compare(Convert.FromHexString(hex), to) <= 0;
        Assert.Equal(before.Keys.Where(k => !InRange(k)).OrderBy(k => k), keys.Where(k => !InRange(k)).OrderBy(k => k));
        Assert.Equal(new[] { Convert.ToHexString(edited), Convert.ToHexString(late) }.OrderBy(k => k), keys.Where(InRange).OrderBy(k => k));

        // And once the delete is through, the week reads like the row database.
        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)),
            store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)), "after the delete");
    }

    [Fact]
    public void RangeDeleteBetweenTheTwoIteratorsOfARead_NeverShowsAChunkRowTheOverlayHid()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(17));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunk = DenseChunks(pair, sensors.Dense)[1];
        var from = Key(sensors.Dense, chunk.FirstTick);
        var to = Key(sensors.Dense, chunk.FirstTick + 30 * TimeSpan.TicksPerSecond);
        var edited = store.GetKeysValuesTo(from, to).Reverse().ElementAt(20).key;

        // The sealed row at `edited` exists; an overlay edit hides it.
        var editedValue = TestValues.Bytes(SensorType.Double, DbKey.FromBytes(edited).Timestamp, new Random(6));
        store.PutSensorValue(edited, editedValue);

        var before = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToDictionary(r => Convert.ToHexString(r.key), r => r.value);

        // The whole delete, chunks and overlay, lands after the read took its overlay view and before its chunk view.
        store.BetweenReadIterators = () =>
        {
            store.BetweenReadIterators = null;
            store.RemoveSensorValues(from, to);
        };

        var read = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();

        Assert.Null(store.BetweenReadIterators);
        Assert.Equal(read.Count, read.Select(r => Convert.ToHexString(r.key)).Distinct().Count());
        foreach (var (key, value) in read)
            Assert.Equal(before[Convert.ToHexString(key)], value);

        Assert.Contains(read, r => r.key.AsSpan().SequenceEqual(edited));
        Assert.Empty(store.GetValuesFrom(from, to));
    }

    #endregion

    #region The compaction marker (#1801)

    [Fact]
    public void ChunkOnlyDelete_ClearsTheCompactionMarker_AndAnEmptyOneDoesNot()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(16));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunk = DenseChunks(pair, sensors.Dense)[1];

        store.Compact();
        Assert.True(store.TryMarkCompacted(store.ChangeCount, "test"));
        Assert.True(CompactedShardMarker.ExistsIn(pair.SealedRowPath));

        // Nothing in the range: nothing changes.
        store.RemoveSensorValues(Key(sensors.Dense, chunk.LastTick + 1), Key(sensors.Dense, chunk.LastTick + 2));
        Assert.True(store.IsCompacted);

        // Rows only in chunks (the overlay holds none of them): the week changed.
        var changes = store.ChangeCount;
        store.RemoveSensorValues(Key(sensors.Dense, chunk.FirstTick), Key(sensors.Dense, chunk.FirstTick + TimeSpan.TicksPerMinute));
        Assert.False(store.IsCompacted);
        Assert.False(CompactedShardMarker.ExistsIn(pair.SealedRowPath));
        Assert.True(store.ChangeCount > changes);

        // A write to the overlay clears it as before.
        Assert.True(store.TryMarkCompacted(store.ChangeCount, "test"));
        store.PutSensorValue(Key(sensors.Dense, chunk.FirstTick), TestValues.Bytes(SensorType.Double, chunk.FirstTick, new Random(5)));
        Assert.False(store.IsCompacted);

        // A reopened week keeps the marker state of its row directory.
        Assert.True(store.TryMarkCompacted(store.ChangeCount, "test"));
        pair.Reopen();
        Assert.True(pair.Sealed.IsCompacted);
    }

    #endregion
}
