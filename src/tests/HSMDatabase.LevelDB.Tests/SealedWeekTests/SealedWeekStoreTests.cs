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
        Assert.Equal(3, store.ChunkReadFailures);
        Assert.True(SealedHistoryDiagnostics.ChunkReadFailures >= globalBefore + 3);

        // The newest page lies in the last chunk and reads; going on into the corrupt chunk throws.
        var newestFirst = store.GetValuesTo(Min(sensors.Dense), Max(sensors.Dense)).GetEnumerator();
        for (int i = 0; i < chunks[2].Rows; i++)
            Assert.True(newestFirst.MoveNext());
        Assert.Throws<SealedChunkReadException>(() => newestFirst.MoveNext());
        newestFirst.Dispose();

        // Ranges that do not reach it read like the row database.
        pair.Compare.Values(pair.Plain.GetValuesFrom(Key(sensors.Dense, chunks[0].FirstTick), Key(sensors.Dense, chunks[0].LastTick)),
            store.GetValuesFrom(Key(sensors.Dense, chunks[0].FirstTick), Key(sensors.Dense, chunks[0].LastTick)), "first chunk");

        // A point read inside it fails too, rather than answering "no value" (DatabaseCore would take another week's).
        var failures = store.ChunkReadFailures;
        var prefix = Key(sensors.Dense, 0).AsSpan(0, 16).ToArray();
        Assert.Throws<SealedChunkReadException>(() => store.GetLatest(Key(sensors.Dense, corrupt.LastTick), prefix));
        Assert.Throws<SealedChunkReadException>(() => store.GetFirst(Key(sensors.Dense, corrupt.FirstTick + 1), prefix));
        Assert.Throws<SealedChunkReadException>(() => store.Get(Key(sensors.Dense, corrupt.FirstTick + 1), prefix));
        Assert.Equal(failures + 3, store.ChunkReadFailures);

        // The history size is informational: it does not throw. The unreadable chunk adds its stored bytes but no
        // rows (its header is unreadable too), so the count is a lower bound. A late value inside the unreadable
        // chunk's span is an overlay row and counts.
        store.PutSensorValue(Key(sensors.Dense, corrupt.FirstTick + 1), TestValues.Bytes(SensorType.Double, corrupt.FirstTick + 1, new Random(1)));

        var (count, keyBytes, valueBytes) = store.CalculateHistorySize(Min(sensors.Dense), Max(sensors.Dense));
        Assert.Equal(chunks[0].Rows + chunks[2].Rows + 1, count);
        Assert.Equal(4 * 24, keyBytes);
        Assert.True(valueBytes > 0);
        Assert.True(store.ChunkReadFailures > failures + 3);
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
        var changes = store.ReclaimableChangeCount;

        // From inside the first chunk to inside the corrupt one: the corrupt chunk would have to be re-encoded.
        var first = DenseChunks(pair, sensors.Dense)[0];
        store.RemoveSensorValues(Key(sensors.Dense, first.FirstTick + 1), Key(sensors.Dense, corrupt.FirstTick + 10 * TimeSpan.TicksPerSecond));

        Assert.True(SealedHistoryDiagnostics.ChunkRewriteFailures > refusedBefore);
        Assert.Equal(changes, store.ReclaimableChangeCount);
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
    public void CorruptChunk_CostsOnlyItsOwnSensorTheStartupValues()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(20));

        using var pair = WeekPair.Create(seed, beforeOpen: p => CorruptChunk(p, DenseChunks(p, sensors.Dense)[2]));

        var store = pair.SealedStore;
        var others = sensors.All.Where(s => s != sensors.Dense).ToList();

        // Latest values: the dense sensor is left unfilled, every other sensor is filled.
        Dictionary<byte[], (long, byte[], byte[])> Request() =>
            sensors.All.ToDictionary(id => new DbKey(id, 0).ToPrefixBytes(), id => (0L, new DbKey(id, DateTime.MaxValue.Ticks).ToBytes(), (byte[])null));

        var plainFill = Request();
        var sealedFill = Request();
        pair.Plain.FillLatestValues(plainFill);
        store.FillLatestValues(sealedFill);

        foreach (var id in sensors.All)
        {
            var prefix = new DbKey(id, 0).ToPrefixBytes();
            var expected = plainFill.Single(kv => kv.Key.AsSpan().SequenceEqual(prefix)).Value.Item3;
            var actual = sealedFill.Single(kv => kv.Key.AsSpan().SequenceEqual(prefix)).Value.Item3;

            if (id == sensors.Dense)
                Assert.Null(actual);
            else
                pair.Compare.Value(expected, actual, $"latest of {id}");
        }

        // First and last values: what the caller accumulated from other weeks is kept, every other sensor is answered.
        var fromOtherWeeks = new Dictionary<Guid, (byte[], byte[])> { [sensors.Dense] = ([1], [2]), [new Guid("51e0b7aa-0000-4000-8000-0000000000bb")] = ([3], [4]) };
        var expectedOthers = pair.Plain.GetLastAndFirstValues(others);
        var result = store.GetLastAndFirstValues(sensors.All, fromOtherWeeks);

        Assert.Same(fromOtherWeeks, result);
        Assert.Equal([1], result[sensors.Dense].Item1);
        Assert.Equal([2], result[sensors.Dense].Item2);
        foreach (var (id, (first, last)) in expectedOthers)
        {
            pair.Compare.Value(first, result[id].Item1, $"first of {id}");
            pair.Compare.Value(last, result[id].Item2, $"last of {id}");
        }
    }

    [Fact]
    public void CorruptLastChunk_IsDeletedByARetentionDeleteThatReachesTheEndOfTheWeek()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(19));
        SealedChunk corrupt = default;

        using var pair = WeekPair.Create(seed, beforeOpen: p => corrupt = CorruptChunk(p, DenseChunks(p, sensors.Dense)[2]));

        var store = pair.SealedStore;

        // A cutoff inside the week, past every row of the chunk: its rows cannot be proven inside without reading it.
        // A prefix delete (#1805) is not refused for it: the chunks before it go, the watermark covers the cutoff, and
        // the unreadable chunk stays, failing the sensor's reads that reach it as before.
        var refused = SealedHistoryDiagnostics.ChunkRewriteFailures;
        store.RemoveSensorValues(Min(sensors.Dense), Key(sensors.Dense, corrupt.LastTick + 1));
        Assert.Equal(refused, SealedHistoryDiagnostics.ChunkRewriteFailures);
        Assert.Equal(1, store.PrefixDeletes);
        Assert.Throws<SealedChunkReadException>(() => store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)).ToList());

        // A cutoff at the end of the week (retention of an older week passes a later one): every row of the week is before it.
        pair.Remove(Min(sensors.Dense), Key(sensors.Dense, WeekPair.WeekTo));

        Assert.Equal(refused, SealedHistoryDiagnostics.ChunkRewriteFailures);
        Assert.Empty(store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)));
        pair.AssertSameReads(sensors.All, new Random(19), probesPerSensor: 3);
    }

    // Sensor removal clears MinValue..MaxValue through ToUniversalTime(): on a host ahead of UTC the upper bound
    // is MaxValue minus the offset, short of sensor|DateTime.MaxValue but past the end of every week.
    [Fact]
    public void CorruptLastChunk_IsDeletedBySensorRemovalOnAHostAheadOfUtc()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(22));

        using var pair = WeekPair.Create(seed, beforeOpen: p => CorruptChunk(p, DenseChunks(p, sensors.Dense)[2]));

        var refused = SealedHistoryDiagnostics.ChunkRewriteFailures;
        pair.Remove(Min(sensors.Dense), Key(sensors.Dense, DateTime.MaxValue.Ticks - 3 * TimeSpan.TicksPerHour));

        Assert.Equal(refused, SealedHistoryDiagnostics.ChunkRewriteFailures);
        Assert.Empty(pair.Sealed.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)));
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

    // A read of sensor B that lands backwards on the rows of the sensor A before it (B has no row at or before the
    // target) must learn "this is not B's row" from the sensor prefix, without ordering A's overlay row against
    // A's corrupt chunk. During the keep period every sealed sensor has overlay rows.
    [Fact]
    public void CorruptLastChunk_WithOverlayRows_DoesNotReachTheIdleSensorAfterIt()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(23));
        var a = sensors.Dense;
        var b = NextId(a);

        using var pair = WeekPair.Create(seed, keepSourceRows: true, beforeOpen: p => CorruptChunk(p, DenseChunks(p, a)[^1]));

        var store = pair.SealedStore;
        var prefix = new DbKey(b, 0).ToPrefixBytes();

        void SameAsPlain(long target, string what)
        {
            var targetKey = Key(b, target);

            pair.Compare.Value(pair.Plain.GetLatest(targetKey, prefix), store.GetLatest(targetKey, prefix), $"{what}: GetLatest");
            pair.Compare.Value(pair.Plain.GetFirst(Min(b), prefix), store.GetFirst(Min(b), prefix), $"{what}: GetFirst");
            pair.Compare.Value(pair.Plain.Get(targetKey, prefix), store.Get(targetKey, prefix), $"{what}: Get");
            pair.Compare.Values(pair.Plain.GetValuesFrom(Min(b), targetKey), store.GetValuesFrom(Min(b), targetKey), $"{what}: oldest first");
            pair.Compare.Values(pair.Plain.GetValuesTo(Min(b), targetKey), store.GetValuesTo(Min(b), targetKey), $"{what}: newest first");
            pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(b), targetKey), store.GetKeysValuesTo(Min(b), targetKey), $"{what}: keys newest first");

            Dictionary<byte[], (long, byte[], byte[])> Request() => new() { [prefix] = (0L, targetKey, null) };
            var plainFill = Request();
            var sealedFill = Request();
            pair.Plain.FillLatestValues(plainFill);
            store.FillLatestValues(sealedFill);
            pair.Compare.Value(plainFill[prefix].Item3, sealedFill[prefix].Item3, $"{what}: FillLatestValues");
        }

        // (i) B has no row in the week.
        SameAsPlain(DateTime.MaxValue.Ticks, "no rows");
        SameAsPlain(WeekPair.WeekFrom + TimeSpan.TicksPerHour, "no rows, inside the week");

        // (ii) B has rows only after the target.
        foreach (var tick in new[] { WeekPair.WeekTo - TimeSpan.TicksPerHour, WeekPair.WeekTo - TimeSpan.TicksPerMinute })
            pair.Put(Key(b, tick), TestValues.Bytes(SensorType.Integer, tick, new Random(3)));

        SameAsPlain(WeekPair.WeekFrom + TimeSpan.TicksPerHour, "rows after the target");

        Assert.Equal(0, store.ChunkReadFailures);
    }

    // The id right after `id` in key order: no sensor of the seed sorts between them.
    private static Guid NextId(Guid id)
    {
        var bytes = new DbKey(id, 0).ToPrefixBytes();

        for (int i = bytes.Length - 1; i >= 0 && ++bytes[i] == 0; i--)
        {
        }

        return new Guid(bytes);
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
    public void ReSeal_AfterAnInterruptedSeal_StartsFromAnEmptyChunkDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hsm-1803-{Guid.NewGuid():N}");
        var rowPath = Path.Combine(root, "SensorValuesV2_1_2");
        var sealedPath = Path.Combine(root, $"{SealedWeekMarker.DirectoryPrefix}_1_2");

        try
        {
            var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(21));

            using (var rows = new LevelDBDatabaseAdapter(rowPath))
                foreach (var (key, value) in seed)
                    rows.Put(key, value);

            // The first attempt wrote every chunk and stopped before its marker.
            WeekSealer.Seal(rowPath, sealedPath, skipMarker: "crash");

            // Between the attempts: one sensor's rows are deleted, another loses its first row (so its first chunk key moves).
            var gone = seed.Where(r => DbKey.FromBytes(r.key).SensorId == sensors.IntegerBar).Select(r => r.key).ToList();
            gone.Add(seed.First(r => DbKey.FromBytes(r.key).SensorId == sensors.String).key);
            using (var rows = new LevelDBDatabaseAdapter(rowPath))
                rows.PutBatch([], gone);

            var goneKeys = gone.Select(Convert.ToHexString).ToHashSet();
            var expected = seed.Select(r => Convert.ToHexString(r.key)).Where(k => !goneKeys.Contains(k)).ToList();

            // The second attempt completes; its sealed rows leave the row database.
            WeekSealer.Seal(rowPath, sealedPath);

            using var db = LevelDBManager.GetSensorValuesDatabaseInstance(rowPath, sealedPath, 1, 2);

            Assert.IsType<SealedSensorValuesDatabase>(db);
            Assert.Equal(expected, db.GetAll().Select(r => Convert.ToHexString(r.key)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Open deletes what an interrupted seal left; handed another directory by mistake (the row database, the
    // database folder) it must touch nothing.
    [Theory]
    [InlineData("SensorValuesV2_1_2")]
    [InlineData("Databases")]
    [InlineData("SensorValuesSealedV1")]
    public void Writer_RefusesADirectoryThatIsNotASealedWeekDirectory(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), $"hsm-1803-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, name);
        var file = Path.Combine(directory, "data.ldb");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(file, "not a chunk database");

            Assert.Throws<ArgumentException>(() => SealedWeekWriter.Open(directory));
            Assert.True(File.Exists(file));
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

    #region Prefix deletes and the delete watermark (#1805)

    // A prefix of the dense sensor up to inside its second chunk: the first chunk goes, the second gets the watermark.
    private static (byte[] from, byte[] to) Prefix(Guid dense, List<SealedChunk> chunks) => (Min(dense), Key(dense, chunks[1].FirstTick + 100 * TimeSpan.TicksPerSecond));

    private static byte[] WatermarkKey(Guid sensor) => sensor.ToByteArray();

    [Fact]
    public void ReaderStartedBeforeAPrefixDelete_SeesTheWeekAsItWasBefore()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1851));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var (from, to) = Prefix(sensors.Dense, DenseChunks(pair, sensors.Dense));
        var before = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();

        // One reader oldest first, in the first chunk; one newest first, in the last chunk.
        using var oldest = store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)).GetEnumerator();
        using var newest = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).GetEnumerator();
        var seenOldest = new List<byte[]>();
        var seenNewest = new List<(byte[] key, byte[] value)>();

        for (int i = 0; i < 10; i++)
        {
            Assert.True(oldest.MoveNext() && newest.MoveNext());
            seenOldest.Add(oldest.Current);
            seenNewest.Add(newest.Current);
        }

        pair.Remove(from, to);
        Assert.Equal(1, store.PrefixDeletes);

        while (oldest.MoveNext())
            seenOldest.Add(oldest.Current);
        while (newest.MoveNext())
            seenNewest.Add(newest.Current);

        Assert.Equal(before.Select(r => r.key), seenNewest.Select(r => r.key));
        Assert.Equal(before.Select(r => r.value), seenNewest.Select(r => r.value));
        Assert.Equal(before.Select(r => r.value).Reverse(), seenOldest);

        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)),
            store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)), "after the prefix delete");
    }

    [Fact]
    public void ReaderBetweenTheWatermarkAndTheOverlayDelete_SeesOnlyRowsTheWeekHeldBefore()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1852));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var (from, to) = Prefix(sensors.Dense, chunks);

        // In the range: a sealed row hidden by an overlay edit, and a late value only in the overlay.
        var edited = Key(sensors.Dense, chunks[1].FirstTick + 10 * TimeSpan.TicksPerSecond);
        var late = Key(sensors.Dense, chunks[0].FirstTick + 1);
        pair.Put(edited, TestValues.Bytes(SensorType.Double, DbKey.FromBytes(edited).Timestamp, new Random(3)));
        pair.Put(late, TestValues.Bytes(SensorType.Double, chunks[0].FirstTick + 1, new Random(4)));

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
        Assert.Equal(1, store.PrefixDeletes);

        var keys = newestFirst.Select(r => Convert.ToHexString(r.key)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(newestFirst.Select(r => r.value).Reverse(), oldestFirst);
        foreach (var (key, value) in newestFirst)
            Assert.Equal(before[Convert.ToHexString(key)], value);

        // Outside the range every row; inside only the overlay rows the delete has not reached yet.
        bool InRange(string hex) => ByteOrder.Instance.Compare(Convert.FromHexString(hex), from) >= 0 && ByteOrder.Instance.Compare(Convert.FromHexString(hex), to) <= 0;
        Assert.Equal(before.Keys.Where(k => !InRange(k)).OrderBy(k => k), keys.Where(k => !InRange(k)).OrderBy(k => k));
        Assert.Equal(new[] { Convert.ToHexString(edited), Convert.ToHexString(late) }.OrderBy(k => k), keys.Where(InRange).OrderBy(k => k));

        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)),
            store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)), "after the prefix delete");
    }

    [Fact]
    public void PrefixDeleteBetweenTheTwoIteratorsOfARead_NeverShowsARowTwice_OrOneTheWeekDidNotHold()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1853));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var (from, to) = Prefix(sensors.Dense, chunks);
        var edited = store.GetKeysValuesTo(Key(sensors.Dense, chunks[1].FirstTick), to).Reverse().ElementAt(20).key;
        store.PutSensorValue(edited, TestValues.Bytes(SensorType.Double, DbKey.FromBytes(edited).Timestamp, new Random(6)));

        var before = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToDictionary(r => Convert.ToHexString(r.key), r => r.value);

        store.BetweenReadIterators = () =>
        {
            store.BetweenReadIterators = null;
            store.RemoveSensorValues(from, to);
        };

        var read = store.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();

        Assert.Null(store.BetweenReadIterators);
        Assert.Equal(1, store.PrefixDeletes);
        Assert.Equal(read.Count, read.Select(r => Convert.ToHexString(r.key)).Distinct().Count());
        foreach (var (key, value) in read)
            Assert.Equal(before[Convert.ToHexString(key)], value);

        Assert.Contains(read, r => r.key.AsSpan().SequenceEqual(edited)); // the overlay view predates the delete
        Assert.Empty(store.GetValuesFrom(from, to));
    }

    // A crash between the chunk batch and the overlay delete (the seam's exception is swallowed by the delete, as any
    // failure of it is): after a reopen every row read is one the week held before, every row the delete keeps is
    // there, and repeating the delete (retention runs again) finishes it.
    [Fact]
    public void APrefixDeleteInterruptedAfterItsWatermark_ReadsCorrect_AndIsFinishedByTheNextDelete()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1854));

        using var pair = WeekPair.Create(seed);

        var (from, to) = Prefix(sensors.Dense, DenseChunks(pair, sensors.Dense));
        var before = pair.Sealed.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToDictionary(r => Convert.ToHexString(r.key), r => r.value);

        pair.SealedStore.AfterChunkRewrite = () => throw new IOException("simulated crash after the watermark");
        pair.Remove(from, to);
        pair.Reopen();

        var after = pair.Plain.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).Select(r => Convert.ToHexString(r.key)).ToHashSet();
        var read = pair.Sealed.GetKeysValuesTo(Min(sensors.Dense), Max(sensors.Dense)).ToList();

        foreach (var (key, value) in read)
            Assert.Equal(before[Convert.ToHexString(key)], value);
        Assert.Superset(after, read.Select(r => Convert.ToHexString(r.key)).ToHashSet());

        pair.Remove(from, to);
        pair.AssertSameReads(sensors.All, new Random(1854), probesPerSensor: 4);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("version")]
    [InlineData("length")]
    public void AnUnreadableWatermark_FailsOnlyItsSensor_IsCounted_AndSensorRemovalClearsIt(string damage)
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1855));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var (from, to) = Prefix(sensors.Dense, chunks);
        pair.Remove(from, to);

        var key = WatermarkKey(sensors.Dense);
        var value = store.ChunkDatabase.Native.Get(key);
        Assert.Equal(SensorWatermark.ValueLength, value.Length);

        switch (damage)
        {
            case "checksum": value[3] ^= 0x01; break;
            case "version": value[0] = 2; break;
            default: value = value[..^1]; break;
        }

        store.ChunkDatabase.Put(key, value);

        // The sensor's reads that need its chunk rows fail and are counted; they never return rows below the watermark.
        var failures = SealedHistoryDiagnostics.ChunkReadFailures;
        Assert.Throws<SealedChunkReadException>(() => store.GetValuesFrom(Min(sensors.Dense), Max(sensors.Dense)).ToList());
        Assert.Throws<SealedChunkReadException>(() => store.GetFirst(Min(sensors.Dense), sensors.Dense.ToByteArray()));
        Assert.True(SealedHistoryDiagnostics.ChunkReadFailures > failures);

        // Every other sensor reads as before, also the reads that pass by the dense sensor's chunks.
        var others = sensors.All.Where(s => s != sensors.Dense).ToList();
        var random = new Random(1855);
        foreach (var sensor in others)
        {
            var prefix = sensor.ToByteArray();

            pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(sensor), Max(sensor)), store.GetKeysValuesTo(Min(sensor), Max(sensor)), $"newest first {sensor}");
            pair.Compare.Values(pair.Plain.GetValuesFrom(Min(sensor), Max(sensor)), store.GetValuesFrom(Min(sensor), Max(sensor)), $"oldest first {sensor}");
            pair.Compare.Value(pair.Plain.GetLatest(Max(sensor), prefix), store.GetLatest(Max(sensor), prefix), $"latest {sensor}");
            pair.Compare.Value(pair.Plain.GetFirst(Min(sensor), prefix), store.GetFirst(Min(sensor), prefix), $"first {sensor}");
        }

        // A delete that would have to raise it is refused, and changes nothing.
        var refused = SealedHistoryDiagnostics.ChunkRewriteFailures;
        store.RemoveSensorValues(to, Key(sensors.Dense, chunks[1].FirstTick + 200 * TimeSpan.TicksPerSecond));
        Assert.Equal(refused + 1, SealedHistoryDiagnostics.ChunkRewriteFailures);
        Assert.Equal(value, store.ChunkDatabase.Native.Get(key));

        // Sensor removal deletes every chunk of the sensor, and the watermark with them.
        pair.Remove(Min(sensors.Dense), Max(sensors.Dense));
        Assert.Null(store.ChunkDatabase.Native.Get(key));
        pair.AssertSameReads(sensors.All, random, probesPerSensor: 4);
    }

    // Review W1: a read of sensor S lands on the rows of the next sensor T and learns from their prefix that they are not
    // S's. It must not decide which of T's rows comes first (T's overlay row or its chunk row), which reads T's delete
    // watermark and decodes T's partly deleted chunk: on healthy data that is a decode per latest-value read, on damaged
    // data a failure of S's reads. The overlay is not empty (the keep period), as on a server: T has overlay rows and a
    // chunk under a watermark, the normal state of a week retention is trimming.
    [Theory]
    [InlineData("healthy")]
    [InlineData("watermark")]
    [InlineData("chunk")]
    public void ReadsOfASensor_NeverReadTheNextSensorsWatermarkOrChunk(string damage)
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1861));

        using var pair = WeekPair.Create(seed, keepSourceRows: true);

        var store = pair.SealedStore;
        var byKey = sensors.All.OrderBy(id => id.ToByteArray(), ByteOrder.Instance).ToList();
        var withRows = byKey.Where(id => pair.Plain.GetValuesFrom(Min(id), Max(id)).Any()).ToList();

        // T: a sealed sensor with another sensor with rows before it; S: that sensor.
        var t = withRows.Skip(1).First(id => pair.Seal.Chunks.Any(c => c.SensorId == id));
        var s = withRows[withRows.IndexOf(t) - 1];
        var tChunk = pair.Seal.Chunks.Where(c => c.SensorId == t).OrderBy(c => c.FirstTick).First();
        var tTicks = pair.Plain.GetValuesFrom(Min(t), Key(t, tChunk.LastTick)).Count();

        // A prefix of T into its first chunk: the chunk straddles the cutoff, T's overlay keeps the rows after it.
        var cutoff = pair.Plain.GetKeysValuesTo(Min(t), Key(t, tChunk.LastTick)).Reverse().ElementAt(tTicks / 3).key;
        pair.Remove(Min(t), cutoff);
        Assert.Equal(DbKey.FromBytes(cutoff).Timestamp, store.ChunkReader.ReadWatermark(store.ChunkDatabase.Native, null, t.ToByteArray()));
        Assert.NotEmpty(((IntervalDataseBase)store).RowDatabase.GetValueKeyPairFromTo(Min(t), Max(t)));

        if (damage == "watermark")
        {
            var value = store.ChunkDatabase.Native.Get(WatermarkKey(t));
            value[5] ^= 0x10;
            store.ChunkDatabase.Native.Put(WatermarkKey(t), value);
        }
        else if (damage == "chunk")
        {
            var key = Key(t, tChunk.FirstTick);
            var chunk = store.ChunkDatabase.Native.Get(key);
            chunk[^1] ^= 0xFF;
            store.ChunkDatabase.Native.Put(key, chunk);
        }

        var touchedT = new List<string>();
        var tPrefix = t.ToByteArray();
        store.ChunkReader.OnDecode = key => { if (key.AsSpan(0, 16).SequenceEqual(tPrefix)) touchedT.Add("decode"); };
        store.ChunkReader.OnWatermarkRead = key => { if (key.AsSpan().SequenceEqual(tPrefix)) touchedT.Add("watermark"); };
        var failures = store.ChunkReadFailures;

        var sPrefix = s.ToByteArray();
        var sTicks = pair.Plain.GetKeysValuesTo(Min(s), Max(s)).Select(kv => DbKey.FromBytes(kv.key).Timestamp).Reverse().ToList();
        var probes = new List<byte[]> { Min(s), Max(s), Key(s, WeekPair.WeekTo), Key(s, sTicks[0] + 1), Key(s, sTicks[^1] + 1), Key(s, sTicks[sTicks.Count / 2]) };

        foreach (var probe in probes)
        {
            var at = DbKey.FromBytes(probe).ToString();
            pair.Compare.Value(pair.Plain.GetLatest(probe, sPrefix), store.GetLatest(probe, sPrefix), $"GetLatest {at}");
            pair.Compare.Value(pair.Plain.GetFirst(probe, sPrefix), store.GetFirst(probe, sPrefix), $"GetFirst {at}");
            pair.Compare.Value(pair.Plain.Get(probe, sPrefix), store.Get(probe, sPrefix), $"Get {at}");
        }

        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(s), Max(s)), store.GetKeysValuesTo(Min(s), Max(s)), "newest first");
        pair.Compare.Values(pair.Plain.GetValuesFrom(Min(s), Max(s)), store.GetValuesFrom(Min(s), Max(s)), "oldest first");
        pair.Compare.Pairs(pair.Plain.GetKeysValuesTo(Min(s), Key(s, WeekPair.WeekTo)).Take(5), store.GetKeysValuesTo(Min(s), Key(s, WeekPair.WeekTo)).Take(5), "a page newest first");

        var plainLastFirst = pair.Plain.GetLastAndFirstValues([s]);
        var sealedLastFirst = store.GetLastAndFirstValues([s]);
        pair.Compare.Value(plainLastFirst[s].Item1, sealedLastFirst[s].Item1, "GetLastAndFirstValues first");
        pair.Compare.Value(plainLastFirst[s].Item2, sealedLastFirst[s].Item2, "GetLastAndFirstValues last");

        foreach (var toTick in new[] { DateTime.MaxValue.Ticks, WeekPair.WeekTo, sTicks[^1] })
        {
            Dictionary<byte[], (long, byte[], byte[])> Request() => new() { [new DbKey(s, 0).ToPrefixBytes()] = (0L, Key(s, toTick), null) };
            var plainFill = Request();
            var sealedFill = Request();
            pair.Plain.FillLatestValues(plainFill);
            store.FillLatestValues(sealedFill);
            pair.Compare.Value(plainFill.Single().Value.Item3, sealedFill.Single().Value.Item3, $"FillLatestValues to {toTick}");
        }

        Assert.Equal(failures, store.ChunkReadFailures);
        Assert.Empty(touchedT);
    }

    // Review W3: the history size with an unreadable chunk falls back to a lower bound, which must not count the rows
    // below a watermark: neither the deleted rows of a readable partly deleted chunk, nor anything of an unreadable one.
    [Theory]
    [InlineData("the last chunk")]
    [InlineData("the partly deleted chunk")]
    public void TheHistorySizeWithAnUnreadableChunk_CountsNoRowBelowTheWatermark(string corrupt)
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1863));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunks = DenseChunks(pair, sensors.Dense);
        var (from, to) = Prefix(sensors.Dense, chunks);
        pair.Remove(from, to);

        var damaged = corrupt == "the last chunk" ? chunks[2] : chunks[1];
        var key = Key(sensors.Dense, damaged.FirstTick);
        var chunk = store.ChunkDatabase.Native.Get(key);
        chunk[^1] ^= 0xFF;
        store.ChunkDatabase.Native.Put(key, chunk);

        // What a lower bound may count: the rows of the readable chunk, live part only (the overlay is empty).
        var readable = corrupt == "the last chunk" ? chunks[1] : chunks[2];
        var expected = pair.Plain.CalculateHistorySize(Key(sensors.Dense, readable.FirstTick), Key(sensors.Dense, readable.LastTick)).count;

        Assert.Equal(expected, store.CalculateHistorySize(Min(sensors.Dense), Max(sensors.Dense)).count);
        Assert.True(expected < pair.Plain.CalculateHistorySize(Min(sensors.Dense), Max(sensors.Dense)).count);
    }

    // Crash images, as in the sealing job's tests (Linux only: on Windows an open LevelDB cannot be copied): the week's
    // directories copied file by file right after the watermark batch, then opened as a week of their own.
    [NotOnWindowsFact]
    public void ACrashImageAfterThePrefixDeleteBatch_OpensCorrect_AndTheNextDeleteFinishesIt()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(1856));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var (from, to) = Prefix(sensors.Dense, DenseChunks(pair, sensors.Dense));
        var before = store.GetAll().ToDictionary(r => Convert.ToHexString(r.key), r => r.value);
        var image = Path.Combine(Path.GetDirectoryName(pair.SealedRowPath), "..", "image");
        var imageRows = Path.Combine(image, Path.GetFileName(pair.SealedRowPath));
        var imageChunks = Path.Combine(image, Path.GetFileName(pair.SealedChunkPath));

        store.AfterChunkRewrite = () =>
        {
            CopyDirectory(pair.SealedRowPath, imageRows);
            CopyDirectory(pair.SealedChunkPath, imageChunks);
        };

        pair.Remove(from, to);

        var after = pair.Plain.GetAll().Select(r => Convert.ToHexString(r.key)).ToHashSet();

        using var copy = LevelDBManager.GetSensorValuesDatabaseInstance(imageRows, imageChunks, WeekPair.WeekFrom, WeekPair.WeekTo);
        Assert.IsType<SealedSensorValuesDatabase>(copy);

        var read = copy.GetAll().ToList();
        foreach (var (key, value) in read)
            Assert.Equal(before[Convert.ToHexString(key)], value);
        Assert.Superset(after, read.Select(r => Convert.ToHexString(r.key)).ToHashSet());

        copy.RemoveSensorValues(from, to);
        pair.Compare.Pairs(pair.Plain.GetAll(), copy.GetAll(), "the crash image after the repeated delete");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from))
            if (Path.GetFileName(file) != "LOCK")
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    private sealed class NotOnWindowsFactAttribute : FactAttribute
    {
        public NotOnWindowsFactAttribute()
        {
            if (OperatingSystem.IsWindows())
                Skip = "LevelDB on Windows opens its log and MANIFEST without read sharing (ERROR_SHARING_VIOLATION): an open database cannot be copied";
        }
    }

    #endregion

    #region The compaction marker (#1801)

    [Fact]
    public void ChunkOnlyDelete_ClearsTheCompactionMarker_AndAnEmptyOneAndAPutDoNot()
    {
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(new Random(16));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var chunk = DenseChunks(pair, sensors.Dense)[1];

        store.Compact();
        Assert.True(store.TryMarkCompacted(store.ReclaimableChangeCount, "test"));
        Assert.True(CompactedShardMarker.ExistsIn(pair.SealedRowPath));

        // Nothing in the range: nothing changes.
        store.RemoveSensorValues(Key(sensors.Dense, chunk.LastTick + 1), Key(sensors.Dense, chunk.LastTick + 2));
        Assert.True(store.IsCompacted);

        // Rows only in chunks (the overlay holds none of them): the week changed.
        var changes = store.ReclaimableChangeCount;
        store.RemoveSensorValues(Key(sensors.Dense, chunk.FirstTick), Key(sensors.Dense, chunk.FirstTick + TimeSpan.TicksPerMinute));
        Assert.False(store.IsCompacted);
        Assert.False(CompactedShardMarker.ExistsIn(pair.SealedRowPath));
        Assert.True(store.ReclaimableChangeCount > changes);

        // A put to the overlay leaves a compaction nothing to reclaim: the marker stays (#1801).
        Assert.True(store.TryMarkCompacted(store.ReclaimableChangeCount, "test"));
        store.PutSensorValue(Key(sensors.Dense, chunk.FirstTick), TestValues.Bytes(SensorType.Double, chunk.FirstTick, new Random(5)));
        Assert.True(store.IsCompacted);
        Assert.True(CompactedShardMarker.ExistsIn(pair.SealedRowPath));

        // A reopened week keeps the marker state of its row directory.
        pair.Reopen();
        Assert.True(pair.Sealed.IsCompacted);
    }

    #endregion
}
