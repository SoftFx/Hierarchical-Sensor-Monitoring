using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.Sealed;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1803: a sealed week answers every read of ISensorValuesDatabase exactly like the row database it was sealed
// from, before and after writes, rewrites, range deletes and sensor-move merges on top of the sealed data.
// Values are compared by bytes, except rows that went through a chunk, which are compared by RowFidelity.
public sealed class SealedWeekDifferentialTests(ITestOutputHelper output)
{
    private const int DenseRows = 10_000; // three chunks of 3,334 rows


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)] // the keep period once removal has begun: the overlay still holds every sealed row, reads merge
    [InlineData(true, true)]  // the keep period as a seal leaves it: reads use the overlay alone (#1805 follow-up)
    public void ScriptedOperations_GiveTheSameReads(bool keepSourceRows, bool overlayOnly)
    {
        var random = new Random(1803);
        var (seed, sensors) = Seed(random);

        using var pair = WeekPair.Create(seed, keepSourceRows, overlayOnly: overlayOnly);

        var dense = sensors.Dense;
        var denseChunks = pair.Seal.Chunks.Where(c => c.SensorId == dense).OrderBy(c => c.FirstTick).ToList();

        Assert.Equal(3, denseChunks.Count);
        Assert.Equal(1, pair.Seal.RefusedSensors); // the File sensor
        pair.AssertSameReads(sensors.All, random);

        // Late values: before, between and after the sealed rows; into the File sensor too.
        foreach (var sensor in sensors.All)
        {
            var type = sensors.Types[sensor];
            pair.Put(Key(sensor, WeekPair.WeekFrom + 1), TestValues.Bytes(type, WeekPair.WeekFrom + 1, random));
            pair.Put(Key(sensor, WeekPair.WeekTo - 1), TestValues.Bytes(type, WeekPair.WeekTo - 1, random));
        }

        pair.Put(Key(dense, denseChunks[1].FirstTick + 1), TestValues.Bytes(SensorType.Double, denseChunks[1].FirstTick + 1, random));
        pair.AssertSameReads(sensors.All, random);

        // Rewrites of sealed keys: the first and last row of a chunk and one inside it.
        foreach (var tick in new[] { denseChunks[0].FirstTick, denseChunks[0].LastTick, denseChunks[1].FirstTick, denseChunks[2].LastTick, Ticks(pair, dense)[5000] })
            pair.Put(Key(dense, tick), TestValues.Bytes(SensorType.Double, tick, random));

        pair.AssertSameReads(sensors.All, random);

        // Range deletes.
        var ticks = Ticks(pair, dense);
        var inside = denseChunks[1];
        Remove(pair, dense, ticks[3500], ticks[3600]);                                  // inside one chunk
        Remove(pair, dense, denseChunks[0].LastTick - 10 * TimeSpan.TicksPerSecond, denseChunks[1].FirstTick + 10 * TimeSpan.TicksPerSecond); // across a chunk border
        Remove(pair, dense, denseChunks[2].FirstTick, denseChunks[2].LastTick);          // exactly one chunk
        Remove(pair, dense, inside.LastTick + 1, inside.LastTick + 2);                   // nothing
        Remove(pair, dense, inside.LastTick, inside.FirstTick);                          // reversed
        pair.AssertSameReads(sensors.All, random);

        pair.Remove(new DbKey(sensors.IntegerBar, 0).ToBytes(), new DbKey(sensors.IntegerBar, DateTime.MaxValue.Ticks).ToBytes()); // whole sensor
        pair.AssertSameReads(sensors.All, random);

        // A border chunk that keeps a single row: its first, then (of another sensor) its last.
        var stringChunk = pair.Seal.Chunks.Single(c => c.SensorId == sensors.String);
        Remove(pair, sensors.String, stringChunk.FirstTick + 1, stringChunk.LastTick);
        var lastChunk = pair.Seal.Chunks.Single(c => c.SensorId == sensors.Last);
        Remove(pair, sensors.Last, lastChunk.FirstTick, lastChunk.LastTick - 1);
        Assert.Contains(stringChunk.FirstTick, Ticks(pair, sensors.String));
        Assert.Contains(lastChunk.LastTick, Ticks(pair, sensors.Last));
        pair.AssertSameReads(sensors.All, random);

        // Across sensors, in key order: from the middle of one sensor to the middle of the next two.
        var ordered = sensors.All.OrderBy(s => new DbKey(s, 0).ToBytes(), ByteOrder.Instance).ToList();
        var a = ordered[2];
        var b = ordered[4];
        pair.Remove(new DbKey(a, Mid(pair, a)).ToBytes(), new DbKey(b, Mid(pair, b)).ToBytes());
        pair.AssertSameReads(sensors.All, random);

        // Sensor-move merges: into an empty sensor, and into one that shares some ticks with the source.
        var target = new Guid("9d2c51e7-0000-4000-8000-0000000000aa");
        pair.Merge(dense, target);

        foreach (var tick in Ticks(pair, sensors.Rate).Take(20))
            pair.Put(Key(sensors.String, tick), TestValues.Bytes(SensorType.String, tick, random));

        pair.Merge(sensors.Rate, sensors.String);
        pair.AssertSameReads([.. sensors.All, target], random);

        // Everything above survives a reopen.
        pair.Reopen();
        pair.AssertSameReads([.. sensors.All, target], random);

        // Sealed rows were read back from chunks; while the source rows are kept, the overlay hides every one.
        output.WriteLine($"fidelity matches: {pair.Compare.FidelityMatches}");
        if (keepSourceRows)
            Assert.Equal(0, pair.Compare.FidelityMatches);
        else
            Assert.True(pair.Compare.FidelityMatches > 0);

        AssertReadMode(pair, overlayOnly);
        AssertMergedReadsAgree(pair, [.. sensors.All, target], random, overlayOnly);
    }

    // Overlay-only: still so after every operation and reopen above, and no read created a chunk iterator. Merged: every
    // read did.
    internal static void AssertReadMode(WeekPair pair, bool overlayOnly)
    {
        Assert.Equal(overlayOnly, pair.SealedStore.ReadsOverlayOnly);

        if (overlayOnly)
            Assert.Equal(0, pair.SealedStore.ChunkIteratorsOpened);
        else
            Assert.True(pair.SealedStore.ChunkIteratorsOpened > 0);
    }

    // After an overlay-only run: the chunks kept up with every change (deletes went to them too), so the same week read
    // merged, as it will be once source-row removal begins, gives the same answers.
    private static void AssertMergedReadsAgree(WeekPair pair, IReadOnlyList<Guid> sensors, Random random, bool overlayOnly)
    {
        if (!overlayOnly)
            return;

        pair.SealedStore.StopOverlayOnlyReads();
        pair.AssertSameReads(sensors, random);
        Assert.True(pair.SealedStore.ChunkIteratorsOpened > 0);
    }

    // #1805: retention, clear history and sensor removal delete a prefix of a sensor's history. On a sealed week such a
    // delete marks a watermark instead of re-encoding the chunk its cutoff falls into; every read must still equal the
    // plain week's, with late writes below and above the watermark, rewrites, middle-range deletes and reopens between.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PrefixDeletes_MarkAWatermark_AndGiveTheSameReads(bool keepSourceRows, bool overlayOnly)
    {
        var random = new Random(1805);
        var (seed, sensors) = Seed(random);

        using var pair = WeekPair.Create(seed, keepSourceRows, overlayOnly: overlayOnly);

        var dense = sensors.Dense;
        var chunks = pair.Seal.Chunks.Where(c => c.SensorId == dense).OrderBy(c => c.FirstTick).ToList();
        var ticks = Ticks(pair, dense);
        long previous = 0;

        // A retention pass: from the previous cutoff (what DatabaseCore passes, the sensor's From) to the new one. It
        // decodes nothing and writes at most the watermark.
        void Retain(long cutoff, string what, long? from = null)
        {
            var store = pair.SealedStore;
            var (decodes, puts, bytes, prefixDeletes) = (store.ChunkReader.Decodes, store.ChunkPuts, store.ChunkBytesWritten, store.PrefixDeletes);

            Remove(pair, dense, from ?? previous, cutoff);
            previous = Math.Max(previous, cutoff);

            Assert.True(store.PrefixDeletes == prefixDeletes + 1, $"{what}: not taken as a prefix delete");
            Assert.True(store.ChunkReader.Decodes == decodes, $"{what}: {store.ChunkReader.Decodes - decodes} chunks decoded");
            Assert.True(store.ChunkPuts - puts <= 1 && store.ChunkBytesWritten - bytes <= KeyOrder.SensorIdLength + SensorWatermark.ValueLength,
                $"{what}: {store.ChunkPuts - puts} puts, {store.ChunkBytesWritten - bytes} bytes written");

            pair.AssertSameReads(sensors.All, random, probesPerSensor: 6);
        }

        long? Watermark() => pair.SealedStore.ChunkReader.ReadWatermark(pair.SealedStore.ChunkDatabase.Native, null, dense.ToByteArray());

        Retain(WeekPair.WeekFrom - 1, "below the sensor's start");
        Assert.Null(Watermark());

        Retain(ticks[1000], "inside the first chunk");
        Assert.Equal(ticks[1000], Watermark());

        Retain(ticks[1000], "the same cutoff twice");
        Retain(ticks[500], "a cutoff that goes backwards", from: 0);
        Assert.Equal(ticks[1000], Watermark()); // never lowered

        // Late writes below and above the watermark, and a rewrite of a live sealed row.
        foreach (var tick in new[] { ticks[10], ticks[999], ticks[1000], ticks[1000] + 1, ticks[1001], ticks[2000] })
            pair.Put(Key(dense, tick), TestValues.Bytes(SensorType.Double, tick, random));
        pair.AssertSameReads(sensors.All, random, probesPerSensor: 6);

        pair.Reopen();
        pair.AssertSameReads(sensors.All, random, probesPerSensor: 6);

        Retain(chunks[1].FirstTick, "exactly at the second chunk's first row");
        Assert.Equal(chunks[1].FirstTick, Watermark());
        Assert.DoesNotContain(ChunkKeys(pair, dense), k => k == chunks[0].FirstTick);

        // A middle-range delete in the chunk that reaches below the watermark rewrites it without its deleted rows.
        Remove(pair, dense, ticks[4000], ticks[4010]);
        pair.AssertSameReads(sensors.All, random, probesPerSensor: 6);

        Retain(chunks[1].LastTick, "exactly at the second chunk's last row");
        Assert.Null(Watermark()); // no chunk reaches below the cutoff any more
        Assert.True(ChunkKeys(pair, dense).All(k => k > chunks[1].LastTick));

        pair.Reopen();
        pair.AssertSameReads(sensors.All, random, probesPerSensor: 6);

        Retain(chunks[2].LastTick - TimeSpan.TicksPerMinute, "inside the last chunk");
        Retain(WeekPair.WeekTo + TimeSpan.TicksPerDay, "past the sensor's end");
        Assert.Empty(ChunkKeys(pair, dense));
        Assert.Null(Watermark());

        // Sensor removal (DateTime.MinValue .. DateTime.MaxValue) and a clear of another sensor to a point in the week.
        pair.Remove(Key(sensors.IntegerBar, 0), Key(sensors.IntegerBar, DateTime.MaxValue.Ticks));
        Remove(pair, sensors.String, 0, Mid(pair, sensors.String));
        pair.AssertSameReads(sensors.All, random);

        pair.Reopen();
        pair.AssertSameReads(sensors.All, random);
        AssertReadMode(pair, overlayOnly);
        AssertMergedReadsAgree(pair, sensors.All, random, overlayOnly);
    }

    // Review W4: cutoffs one tick before a chunk's first and last keys, on a multi-chunk sensor that is the last sensor of
    // the database (no key after its chunks), and on a one-chunk sensor with no next key whose cutoff is inside the week
    // (the end-of-week shortcut does not apply, the verified header decides).
    [Fact]
    public void PrefixDeletes_OneTickBeforeChunkEdges_AndOnTheLastSensor()
    {
        var random = new Random(18053);
        var lastId = new Guid(Enumerable.Repeat((byte)0xFF, 16).ToArray());
        var other = new Guid(Enumerable.Repeat((byte)0x11, 16).ToArray());
        var seed = new List<(byte[] key, byte[] value)>();

        var tick = WeekPair.WeekFrom + TimeSpan.TicksPerMinute;
        for (int i = 0; i < DenseRows; i++, tick += TimeSpan.TicksPerSecond + random.NextInt64(0, 1000))
            seed.Add((Key(lastId, tick), TestValues.Bytes(SensorType.Double, tick, random)));

        for (int i = 0; i < 200; i++)
        {
            var t = WeekPair.WeekFrom + i * TimeSpan.TicksPerMinute;
            seed.Add((Key(other, t), TestValues.Bytes(SensorType.Integer, t, random)));
        }

        seed.Sort((x, y) => ByteOrder.Instance.Compare(x.key, y.key));

        using (var pair = WeekPair.Create(seed))
        {
            var chunks = pair.Seal.Chunks.Where(c => c.SensorId == lastId).OrderBy(c => c.FirstTick).ToList();
            Assert.Equal(3, chunks.Count);

            long previous = 0;

            foreach (var (cutoff, what) in new[]
            {
                (chunks[0].FirstTick - 1, "one tick before the first chunk's first key"),
                (chunks[0].LastTick - 1, "one tick before the first chunk's last key"),
                (chunks[1].FirstTick - 1, "one tick before the second chunk's first key"),
                (chunks[1].LastTick - 1, "one tick before the second chunk's last key"),
                (chunks[2].LastTick - 1, "one tick before the last chunk's last key"),
                (chunks[2].LastTick, "the last chunk's last key"),
            })
            {
                var prefixDeletes = pair.SealedStore.PrefixDeletes;

                Remove(pair, lastId, previous, cutoff);
                previous = cutoff;

                Assert.True(pair.SealedStore.PrefixDeletes == prefixDeletes + 1, $"{what}: not a prefix delete");
                pair.AssertSameReads([lastId, other], random, probesPerSensor: 6);
            }

            Assert.Empty(ChunkKeys(pair, lastId));
            pair.Reopen();
            pair.AssertSameReads([lastId, other], random);
        }

        // A one-chunk sensor with no key after it: the cutoff inside its chunk, one tick before its last key, at it.
        var (standard, sensors) = Seed(new Random(18054));

        using (var pair = WeekPair.Create(standard))
        {
            var chunk = pair.Seal.Chunks.Single(c => c.SensorId == sensors.Last);
            var mid = Mid(pair, sensors.Last);

            Remove(pair, sensors.Last, 0, mid);
            Assert.Equal([chunk.FirstTick], ChunkKeys(pair, sensors.Last));
            pair.AssertSameReads(sensors.All, random, probesPerSensor: 4);

            Remove(pair, sensors.Last, mid, chunk.LastTick - 1);
            Assert.Equal([chunk.FirstTick], ChunkKeys(pair, sensors.Last)); // its last row is still live
            pair.AssertSameReads(sensors.All, random, probesPerSensor: 4);

            Remove(pair, sensors.Last, chunk.LastTick - 1, chunk.LastTick);
            Assert.Empty(ChunkKeys(pair, sensors.Last));
            pair.AssertSameReads(sensors.All, random, probesPerSensor: 4);
        }
    }

    // #1805: a week trimmed hour by hour by retention writes a few bytes per sensor and pass, and stays on disk within its
    // sealed size: a deleted chunk's bytes go at the next compaction, nothing is re-encoded meanwhile.
    [Fact]
    public void HourlyRetention_WritesAFewBytesPerSensor_AndTheChunkDirectoryDoesNotGrow()
    {
        var random = new Random(18052);

        // 20 sensors with one chunk each that spans the whole week (a value every 5 minutes): every hourly cutoff falls
        // inside every sensor's chunk, the case that re-encoded the whole week every hour before #1805.
        var sensors = Enumerable.Range(0, 20).Select(_ => RandomGuid(random)).ToList();
        var seed = new List<(byte[] key, byte[] value)>();

        foreach (var sensor in sensors)
            for (int i = 0; i < 2_000; i++)
            {
                var tick = WeekPair.WeekFrom + i * 5 * TimeSpan.TicksPerMinute + random.NextInt64(0, TimeSpan.TicksPerMinute);
                seed.Add((Key(sensor, tick), TestValues.Bytes(SensorType.Double, tick, random)));
            }

        seed.Sort((x, y) => ByteOrder.Instance.Compare(x.key, y.key));

        using var pair = WeekPair.Create(seed);

        var store = pair.SealedStore;
        var sealedSize = LevelDbBytes(pair.SealedChunkPath);
        var cutoffs = new Dictionary<Guid, long>();
        long largest = 0, marked = 0;

        Assert.Equal(sensors.Count, pair.Seal.Chunks.Count);

        for (var hour = 1; hour <= 166; hour++) // the last values are at 166 h 35 min
        {
            var (bytes, puts, decodes) = (store.ChunkBytesWritten, store.ChunkPuts, store.ChunkReader.Decodes);

            foreach (var sensor in sensors)
            {
                var cutoff = WeekPair.WeekFrom + hour * TimeSpan.TicksPerHour;
                Remove(pair, sensor, cutoffs.GetValueOrDefault(sensor), cutoff);
                cutoffs[sensor] = cutoff;
            }

            Assert.Equal(decodes, store.ChunkReader.Decodes);
            Assert.True(store.ChunkBytesWritten - bytes <= sensors.Count * (KeyOrder.SensorIdLength + SensorWatermark.ValueLength),
                $"hour {hour}: {store.ChunkBytesWritten - bytes} bytes written for {sensors.Count} sensors");

            marked += store.ChunkPuts - puts;
            largest = Math.Max(largest, LevelDbBytes(pair.SealedChunkPath));

            if (hour % 24 == 0)
                pair.AssertSameReads(sensors, random, probesPerSensor: 3);
        }

        pair.AssertSameReads(sensors, random, probesPerSensor: 3);

        output.WriteLine($"sealed {sealedSize} B; largest during a week of hourly retention {largest} B; {marked} watermark puts, " +
                         $"{store.ChunkBytesWritten} B written by deletes, {store.ChunkReader.Decodes} chunks decoded by reads");
        Assert.Equal(sensors.Count * 166, marked); // every pass raised every sensor's watermark
        // The write-ahead log holds the watermark puts and chunk deletes (tens of bytes each); re-encoding the border
        // chunks every hour, as before #1805, grew it by about the week's chunk bytes per pass.
        Assert.True(largest <= 2 * sealedSize + (64 << 10), $"{largest} bytes on disk against {sealedSize} sealed");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RandomOperations_GiveTheSameReads(bool keepSourceRows, bool overlayOnly)
    {
        const int seed = 18030;
        const int steps = 400;

        output.WriteLine($"seed {seed}");

        var random = new Random(seed);
        var (data, sensors) = Seed(random);
        var extra = RandomGuid(random);
        var all = sensors.All.Append(extra).ToList();
        var types = new Dictionary<Guid, SensorType>(sensors.Types) { [extra] = SensorType.Integer };

        using var pair = WeekPair.Create(data, keepSourceRows, overlayOnly: overlayOnly);
        output.WriteLine($"sealed {pair.Seal.SealedRows} rows into {pair.Seal.Chunks.Count} chunks; {pair.Seal.RefusedSensors} sensor(s) refused");

        var cutoffs = new Dictionary<Guid, long>();

        for (int step = 0; step < steps; step++)
        {
            var sensor = all[random.Next(all.Count)];
            var op = random.Next(100);
            string what;

            if (op < 30)
            {
                var tick = WeekPair.WeekFrom + random.NextInt64(0, WeekPair.WeekTo - WeekPair.WeekFrom);
                pair.Put(Key(sensor, tick), TestValues.Bytes(types[sensor], tick, random));
                what = $"put {sensor} {tick}";
            }
            else if (op < 50)
            {
                var ticks = Ticks(pair, sensor);
                if (ticks.Count == 0)
                    continue;

                var tick = ticks[random.Next(ticks.Count)];
                pair.Put(Key(sensor, tick), TestValues.Bytes(types[sensor], tick, random));
                what = $"rewrite {sensor} {tick}";
            }
            else if (op < 62)
            {
                // A prefix delete (#1805): retention from the sensor's previous cutoff, now and then from the start,
                // usually a little further, sometimes the same cutoff or one that goes back.
                var previous = cutoffs.GetValueOrDefault(sensor, WeekPair.WeekFrom);
                var cutoff = previous + random.Next(5) switch
                {
                    0 => 0,
                    1 => -random.NextInt64(0, TimeSpan.TicksPerHour),
                    _ => random.NextInt64(0, TimeSpan.TicksPerHour * 3),
                };
                var from = random.Next(4) == 0 ? 0 : previous;

                Remove(pair, sensor, from, cutoff);
                cutoffs[sensor] = Math.Max(previous, cutoff);
                what = $"prefix delete {sensor} [{from}, {cutoff}]";
            }
            else if (op < 92)
            {
                var (from, to) = RandomRange(pair, all, random);
                pair.Remove(from, to);
                what = $"remove [{DbKey.FromBytes(from)}, {DbKey.FromBytes(to)}]";
            }
            else
            {
                var into = all[random.Next(all.Count)];
                if (into == sensor)
                    continue;

                pair.Merge(sensor, into);
                what = $"merge {sensor} into {into}";
            }

            if (step % 40 == 39)
            {
                pair.AssertSameReads(all, random, probesPerSensor: 4);
                output.WriteLine($"step {step}: {what}; checked {pair.Plain.GetAll().Count()} rows ({pair.Compare.FidelityMatches} fidelity matches so far)");
            }
        }

        output.WriteLine($"prefix deletes: {pair.SealedStore.PrefixDeletes}");
        Assert.True(pair.SealedStore.PrefixDeletes > 0);

        pair.Reopen();
        pair.AssertSameReads(all, random);

        // A run that deleted (almost) everything compares nothing: it must not pass for proof.
        var remaining = pair.Plain.GetAll().Count();
        output.WriteLine($"{remaining} rows remain; fidelity matches: {pair.Compare.FidelityMatches}");
        Assert.True(remaining > data.Count / 2, $"only {remaining} of {data.Count} rows remain");

        // While the source rows are kept, every sealed row is hidden by its overlay row.
        if (keepSourceRows)
            Assert.Equal(0, pair.Compare.FidelityMatches);
        else
            Assert.True(pair.Compare.FidelityMatches > 0);

        AssertReadMode(pair, overlayOnly);
        AssertMergedReadsAgree(pair, all, random, overlayOnly);
    }


    internal sealed record SensorSet(Guid Dense, Guid IntegerBar, Guid String, Guid File, Guid Rate, Guid Last, Guid Empty, List<Guid> All, Dictionary<Guid, SensorType> Types);

    // A week of a dense Double sensor (three chunks), a sensor of every other sealed type, a File sensor, the
    // sensor whose id sorts last, and a sensor without rows.
    internal static (List<(byte[] key, byte[] value)>, SensorSet) Seed(Random random)
    {
        var rows = new List<(byte[], byte[])>();
        var types = new Dictionary<Guid, SensorType>();

        Guid NewSensor(SensorType type, int count, long step, Guid? id = null)
        {
            var sensor = id ?? RandomGuid(random);
            types[sensor] = type;

            var tick = WeekPair.WeekFrom + random.NextInt64(TimeSpan.TicksPerMinute, TimeSpan.TicksPerHour);
            for (int i = 0; i < count; i++)
            {
                rows.Add((new DbKey(sensor, tick).ToBytes(), TestValues.Bytes(type, tick, random)));
                tick += step + random.NextInt64(0, step / 10 + 1);
            }

            return sensor;
        }

        var dense = NewSensor(SensorType.Double, DenseRows, TimeSpan.TicksPerSecond);
        var integerBar = NewSensor(SensorType.IntegerBar, 500, TimeSpan.TicksPerMinute);
        var stringSensor = NewSensor(SensorType.String, 300, TimeSpan.TicksPerMinute);
        var file = NewSensor(SensorType.File, 40, TimeSpan.TicksPerHour);
        var rate = NewSensor(SensorType.Rate, 100, TimeSpan.TicksPerMinute);
        var last = NewSensor(SensorType.Integer, 300, TimeSpan.TicksPerMinute, new Guid(Enumerable.Repeat((byte)0xFF, 16).ToArray()));

        foreach (var type in TestValues.SealedTypes.Except([SensorType.Double, SensorType.IntegerBar, SensorType.String, SensorType.Rate]))
            NewSensor(type, random.Next(20, 200), TimeSpan.TicksPerMinute);

        var empty = RandomGuid(random);
        types[empty] = SensorType.Integer;

        rows.Sort((x, y) => ByteOrder.Instance.Compare(x.Item1, y.Item1));

        return (rows, new SensorSet(dense, integerBar, stringSensor, file, rate, last, empty, [.. types.Keys], types));
    }

    private static Guid RandomGuid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static byte[] Key(Guid sensor, long tick) => new DbKey(sensor, tick).ToBytes();

    // The first ticks of a sensor's chunks as stored now (the watermark key is not a chunk).
    private static List<long> ChunkKeys(WeekPair pair, Guid sensor) =>
        [.. pair.SealedStore.ChunkDatabase.GetAll().Where(kv => kv.Item1.Length == KeyOrder.KeyLength && new Guid(kv.Item1.AsSpan(0, 16)) == sensor).Select(kv => KeyOrder.Tick(kv.Item1))];

    // A LevelDB directory's own files: tables, write-ahead logs, MANIFEST, CURRENT.
    internal static long LevelDbBytes(string directory) =>
        new DirectoryInfo(directory).GetFiles().Where(f => f.Name.EndsWith(".ldb") || f.Name.EndsWith(".sst") || f.Name.EndsWith(".log") && f.Name != "LOG" ||
                                                             f.Name.StartsWith("MANIFEST-") || f.Name == "CURRENT").Sum(f => f.Length);

    private static void Remove(WeekPair pair, Guid sensor, long from, long to) => pair.Remove(Key(sensor, from), Key(sensor, to));

    private static List<long> Ticks(WeekPair pair, Guid sensor) =>
        pair.Plain.GetKeysValuesTo(new DbKey(sensor, 0).ToBytes(), new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes())
            .Select(kv => DbKey.FromBytes(kv.key).Timestamp).Reverse().ToList();

    private static long Mid(WeekPair pair, Guid sensor)
    {
        var ticks = Ticks(pair, sensor);
        return ticks.Count == 0 ? WeekPair.WeekFrom : ticks[ticks.Count / 2];
    }

    // Whole sensors (rarely), ranges inside and across the chunks as sealed, across two neighbouring sensors,
    // empty and reversed ones. Each takes a small bite, so the week keeps sealed rows to compare to the end.
    private static (byte[] from, byte[] to) RandomRange(WeekPair pair, List<Guid> sensors, Random random)
    {
        var sensor = sensors[random.Next(sensors.Count)];

        switch (random.Next(6))
        {
            case 0 when random.Next(4) == 0:
                return (Key(sensor, 0), Key(sensor, DateTime.MaxValue.Ticks));

            case 0:
            case 1:
            case 2:
                {
                    var chunk = pair.Seal.Chunks[random.Next(pair.Seal.Chunks.Count)];
                    var span = Math.Max(1, chunk.LastTick - chunk.FirstTick);
                    var from = chunk.FirstTick + random.NextInt64(-span / 8, span);
                    var to = from + random.NextInt64(0, span / 16 + 1);
                    return (Key(chunk.SensorId, from), Key(chunk.SensorId, to));
                }

            case 3:
                {
                    // From the last rows of one sensor to the first rows of the next one in key order.
                    var ordered = sensors.OrderBy(s => Key(s, 0), ByteOrder.Instance).ToList();
                    var i = ordered.IndexOf(sensor);
                    if (i == ordered.Count - 1)
                        i--;

                    var a = Ticks(pair, ordered[i]);
                    var b = Ticks(pair, ordered[i + 1]);
                    var from = a.Count == 0 ? WeekPair.WeekTo : a[a.Count - 1 - a.Count / 20];
                    var to = b.Count == 0 ? WeekPair.WeekFrom : b[b.Count / 20];
                    return (Key(ordered[i], from), Key(ordered[i + 1], to));
                }

            default:
                {
                    var from = WeekPair.WeekFrom + random.NextInt64(0, WeekPair.WeekTo - WeekPair.WeekFrom);
                    var to = from + random.NextInt64(-TimeSpan.TicksPerMinute, TimeSpan.TicksPerHour);
                    return (Key(sensor, from), Key(sensor, to));
                }
        }
    }
}
