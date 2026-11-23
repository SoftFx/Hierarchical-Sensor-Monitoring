using System.Diagnostics;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.SealedHistory;

// The sealed week store (#1803) against the row database of the same week: the same rows, once as a plain
// week and once sealed (chunks + an overlay holding only what the codec refused), both compacted.
//
//   SealedHistoryBench --store-synthetic <work-dir>
//   SealedHistoryBench --store-shard <SensorValuesV2_* copy> <work-dir>
//
// Sensors are reported by index only.
internal static class StoreBench
{
    private const long WeekFrom = 639_291_744_000_000_000; // 2026-11-02, a Monday
    private const long WeekTo = WeekFrom + 7 * TimeSpan.TicksPerDay;

    private static readonly MemoryPackFormatter _formatter = new();


    public static void Run(Options options)
    {
        var work = Path.GetFullPath(options.StoreWorkPath);
        Directory.CreateDirectory(work);

        var plainRows = Path.Combine(work, "plain", $"SensorValuesV2_{WeekFrom}_{WeekTo}");
        var sealedRows = Path.Combine(work, "sealed", $"SensorValuesV2_{WeekFrom}_{WeekTo}");
        var sealedChunks = Path.Combine(work, "sealed", $"{SealedWeekMarker.DirectoryPrefix}_{WeekFrom}_{WeekTo}");
        var unusedChunks = Path.Combine(work, "plain", $"{SealedWeekMarker.DirectoryPrefix}_{WeekFrom}_{WeekTo}");

        if (options.StoreShardPath is not null)
        {
            Report.Line($"== Store: a copy of a real weekly shard");
            CopyDirectory(Path.GetFullPath(options.StoreShardPath), plainRows);
        }
        else
        {
            Report.Line($"== Store: synthetic week");
            WriteSynthetic(plainRows);
        }

        CopyDirectory(plainRows, sealedRows);

        var seal = Stopwatch.StartNew();
        var (sealedCount, chunkCount, refused) = Seal(sealedRows, sealedChunks);
        Report.Line($"sealed {sealedCount:N0} rows into {chunkCount:N0} chunks in {seal.Elapsed.TotalSeconds:F1} s; {refused} sensor(s) refused (kept as rows)");

        using (var db = new LevelDBDatabaseAdapter(plainRows)) db.Compact();
        using (var db = new LevelDBDatabaseAdapter(sealedRows)) db.Compact();
        using (var db = new LevelDBDatabaseAdapter(sealedChunks)) db.Compact();

        Report.Line($"on disk: plain {Size(plainRows):N0} B; sealed {Size(sealedRows) + Size(sealedChunks):N0} B (overlay {Size(sealedRows):N0}, chunks {Size(sealedChunks):N0})");

        using var plain = LevelDBManager.GetSensorValuesDatabaseInstance(plainRows, unusedChunks, WeekFrom, WeekTo);
        var sensors = plain.GetAll().GroupBy(r => DbKey.FromBytes(r.key).SensorId).Select(g => (Id: g.Key, Rows: g.Count())).ToList();
        var dense = sensors.OrderByDescending(s => s.Rows).First();
        var denseIndex = sensors.IndexOf(dense);
        var min = new DbKey(dense.Id, 0).ToBytes();
        var max = new DbKey(dense.Id, DateTime.MaxValue.Ticks).ToBytes();
        var prefix = new DbKey(dense.Id, 0).ToPrefixBytes();

        Report.Line($"{sensors.Count} sensors, {sensors.Sum(s => s.Rows):N0} rows; densest sensor #{denseIndex}: {dense.Rows:N0} rows");

        using var sealedWeek = LevelDBManager.GetSensorValuesDatabaseInstance(sealedRows, sealedChunks, WeekFrom, WeekTo);

        // The first page right after opening, once: nothing of the week is cached yet.
        var cold = Stopwatch.StartNew();
        var firstSealed = sealedWeek.GetValuesTo(min, max).Take(100).Count();
        Report.Line($"first page of 100, freshly opened sealed week: {cold.Elapsed.TotalMilliseconds:F2} ms ({firstSealed} values)");

        var ids = sensors.Select(s => s.Id).ToList();

        Row("page of 100, newest first (GetValuesTo)", () => plain.GetValuesTo(min, max).Take(100).Count(), () => sealedWeek.GetValuesTo(min, max).Take(100).Count());
        Row("page of 100, oldest first (GetValuesFrom)", () => plain.GetValuesFrom(min, max).Take(100).Count(), () => sealedWeek.GetValuesFrom(min, max).Take(100).Count());
        Row("GetLatest of the densest sensor", () => plain.GetLatest(max, prefix), () => sealedWeek.GetLatest(max, prefix));
        var middle = Middle(plain, min, max);
        Row("Get at a key in the middle", () => plain.Get(middle, prefix), () => sealedWeek.Get(middle, prefix));
        Row("whole densest sensor (GetValuesFrom)", () => plain.GetValuesFrom(min, max).Count(), () => sealedWeek.GetValuesFrom(min, max).Count(), runs: 5);
        // What a sensor's history load asks of each week (DatabaseCore.GetLatestValue and GetFirstValue).
        var loadKeys = ids.Select(id => (Max: new DbKey(id, DateTime.MaxValue.Ticks).ToBytes(), Min: new DbKey(id, 0).ToBytes(), Prefix: new DbKey(id, 0).ToPrefixBytes())).ToList();
        void Load(ISensorValuesDatabase db)
        {
            foreach (var (maxKey, minKey, sensorPrefix) in loadKeys)
            {
                db.GetLatest(maxKey, sensorPrefix);
                db.GetFirst(minKey, sensorPrefix);
            }
        }

        var loadPlain = Report.Time(() => Load(plain), 5);
        var loadSealed = Report.Time(() => Load(sealedWeek), 5);
        Report.Line($"{"history load, latest + first, all " + ids.Count + " sensors",-45} plain {loadPlain,10:F3} ms   sealed {loadSealed,10:F3} ms");
        Report.Line($"{"  per sensor",-45} plain {loadPlain / ids.Count * 1000,10:F1} us   sealed {loadSealed / ids.Count * 1000,10:F1} us");

        Row($"GetLastAndFirstValues, all {ids.Count} sensors (no caller)", () => plain.GetLastAndFirstValues(ids), () => sealedWeek.GetLastAndFirstValues(ids), runs: 5);
        Row("full week iteration (GetAll)", () => plain.GetAll().Count(), () => sealedWeek.GetAll().Count(), runs: 3);
    }


    private static void Row(string what, Func<object> plain, Func<object> sealedRead, int runs = 15)
    {
        var p = Report.Time(() => plain(), runs);
        var s = Report.Time(() => sealedRead(), runs);

        Report.Line($"{what,-45} plain {p,10:F3} ms   sealed {s,10:F3} ms");
    }

    private static byte[] Middle(ISensorValuesDatabase db, byte[] min, byte[] max)
    {
        var keys = db.GetKeysValuesTo(min, max).Select(kv => kv.key).ToList();
        return keys[keys.Count / 2];
    }

    // The test-only seal: chunks of every sensor the codec accepts, verified, the marker, then the rows go.
    private static (long rows, int chunks, int refused) Seal(string rowPath, string chunkPath)
    {
        long rows = 0;
        int chunks = 0, refused = 0;
        var sealedKeys = new List<byte[]>();

        using var rowDb = new LevelDBDatabaseAdapter(rowPath);
        var writer = SealedWeekWriter.Open(chunkPath);

        foreach (var group in rowDb.GetAll().GroupBy(r => DbKey.FromBytes(r.Item1).SensorId))
        {
            var source = group.ToList();
            var history = source.Select(r => new HistoryRow(DbKey.FromBytes(r.Item1).Timestamp, _formatter.Deserialize(r.Item2))).ToList();
            var encoded = ChunkCodec.Encode(history);

            if (!encoded.IsSuccess)
            {
                refused++;
                continue;
            }

            var decoded = encoded.Chunks.SelectMany(c => ChunkCodec.Decode(c)).ToList();
            for (int i = 0; i < history.Count; i++)
                if (RowFidelity.FindDifference(history[i], decoded[i]) is { } field)
                    throw new InvalidOperationException($"verification failed: {field}");

            writer.WriteChunks(group.Key, encoded.Chunks);
            sealedKeys.AddRange(source.Select(r => r.Item1));
            rows += history.Count;
            chunks += encoded.Chunks.Count;
        }

        writer.Complete($"bench seal: {rows} rows, {chunks} chunks");
        writer.Dispose();
        rowDb.PutBatch([], sealedKeys);

        return (rows, chunks, refused);
    }

    // One dense Double sensor (one value per second for three days) and 299 sensors of every type with
    // a value every 1 to 20 minutes, a few of them File sensors.
    private static void WriteSynthetic(string rowPath)
    {
        var random = new Random(1803);
        SensorType[] types = [SensorType.Boolean, SensorType.Integer, SensorType.Double, SensorType.String, SensorType.IntegerBar, SensorType.DoubleBar, SensorType.TimeSpan, SensorType.Version, SensorType.Rate, SensorType.Enum];

        using var db = new LevelDBDatabaseAdapter(rowPath);

        void Sensor(SensorType type, long step, long until)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var id = new Guid(bytes);
            var puts = new List<(byte[], byte[])>();
            double level = 50;

            for (long tick = WeekFrom + random.NextInt64(0, step); tick < until; tick += step + random.NextInt64(-step / 50, step / 50 + 1))
            {
                level = Math.Round(level + random.Next(-100, 101) / 100.0, 2);
                puts.Add((new DbKey(id, tick).ToBytes(), _formatter.Serialize(Value(type, tick, level, random))));

                if (puts.Count == 10_000)
                {
                    db.PutBatch(puts);
                    puts.Clear();
                }
            }

            if (puts.Count > 0)
                db.PutBatch(puts);
        }

        Sensor(SensorType.Double, TimeSpan.TicksPerSecond, WeekFrom + 3 * TimeSpan.TicksPerDay);

        for (int i = 0; i < 299; i++)
            Sensor(i % 50 == 0 ? SensorType.File : types[i % types.Length], TimeSpan.TicksPerMinute * random.Next(1, 21), WeekTo);
    }

    private static BaseValue Value(SensorType type, long tick, double level, Random random)
    {
        var time = new DateTime(tick, DateTimeKind.Utc);

        BaseValue value = type switch
        {
            SensorType.Boolean => new BooleanValue { Value = random.Next(10) == 0 },
            SensorType.Integer => new IntegerValue { Value = (int)(level * 10) },
            SensorType.Double => new DoubleValue { Value = level },
            SensorType.Rate => new RateValue { Value = random.NextDouble() * 10 },
            SensorType.String => new StringValue { Value = $"state {random.Next(5)}" },
            SensorType.TimeSpan => new TimeSpanValue { Value = TimeSpan.FromSeconds(random.Next(100_000)) },
            SensorType.Version => new VersionValue { Value = new Version(1, 2, 3, 4) },
            SensorType.Enum => new EnumValue { Value = random.Next(4) },
            SensorType.IntegerBar => new IntegerBarValue { Count = 60, OpenTime = time.AddMinutes(-1), CloseTime = time, Min = (int)level - 5, Max = (int)level + 5, Mean = (int)level, FirstValue = (int)level, LastValue = (int)level },
            SensorType.DoubleBar => new DoubleBarValue { Count = 60, OpenTime = time.AddMinutes(-1), CloseTime = time, Min = level - 1, Max = level + 1, Mean = level, FirstValue = level, LastValue = level },
            SensorType.File => new FileValue { Value = new byte[256], Name = "report", Extension = "txt", OriginalSize = 256 },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        value = value with { ReceivingTime = time.AddTicks(random.Next(0, 5_000_000)) };
        value.Time = time;
        return value;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from))
            if (Path.GetFileName(file) != "LOCK")
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    private static long Size(string path) => new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}
