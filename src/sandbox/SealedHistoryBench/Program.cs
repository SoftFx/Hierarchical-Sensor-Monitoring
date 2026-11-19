using System.Diagnostics;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using HSMDatabase.SealedHistory;
using MemoryPack;

// Measures the sealed-history chunk codec (#1802) on a history shard and on synthetic dense sensors.
//
//   SealedHistoryBench <shard-dir> [--limits 1024,4096,...] [--out <dir>]
//   SealedHistoryBench --synthetic [--limits ...]
//
// <shard-dir> is a SensorValuesV2_* LevelDB directory. It is opened read-only; work on a copy anyway.
// --out writes the chunks into a fresh LevelDB there (key sensorId | firstKey), compacts it and reports its size.

var options = Options.Parse(args);

if (options.ShardPath is not null)
    ShardBench.Run(options);

if (options.Synthetic)
    SyntheticBench.Run(options);

if (options.ShardPath is null && !options.Synthetic)
    Console.WriteLine("usage: SealedHistoryBench <shard-dir> [--limits 1024,4096] [--out <dir>] | --synthetic [--limits ...]");


internal sealed class Options
{
    public string ShardPath;
    public string OutPath;
    public bool Synthetic;
    public int[] Limits = [ChunkCodec.MaxRowsPerChunk];


    public static Options Parse(string[] args)
    {
        var o = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--synthetic": o.Synthetic = true; break;
                case "--limits": o.Limits = args[++i].Split(',').Select(int.Parse).ToArray(); break;
                case "--out": o.OutPath = args[++i]; break;
                default: o.ShardPath = args[i]; break;
            }
        }

        return o;
    }
}


internal static class Report
{
    public static void Line(string text = "") => Console.WriteLine(text);

    public static double Median(List<double> xs)
    {
        xs.Sort();
        return xs[xs.Count / 2];
    }

    /// <summary>Median wall time of <paramref name="action"/> in milliseconds, after a warm-up.</summary>
    public static double Time(Action action, int runs = 15)
    {
        action();
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        return Median(times);
    }
}


internal static class Compare
{
    /// <summary>MemoryPack bytes of the source with its ReceivingTime in stored form, against the decoded value's bytes.</summary>
    public static bool SameMemoryPackBytes(HistoryRow source, HistoryRow decoded) =>
        MemoryPackSerializer.Serialize(source.Value with { ReceivingTime = RowFidelity.SealedReceivingTime(source.Value) }).AsSpan()
            .SequenceEqual(MemoryPackSerializer.Serialize(decoded.Value));

    /// <summary>True when a Nullable member of the value carries non-zero padding bytes (MemoryPack persists them).</summary>
    // Not optimised: an optimised build may copy a Nullable field by field and drop the very bytes this looks for.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    public static bool HasNullablePadding(BaseValue value)
    {
        bool any = Padded(value.LastReceivingTime);

        if (value is BaseInstantValue instant)
            any |= Padded(instant.EmaValue);

        if (value is BarBaseValue bar)
            any |= Padded(bar.EmaMin) | Padded(bar.EmaMax) | Padded(bar.EmaMean) | Padded(bar.EmaCount);

        if (value is IntegerBarValue ib)
            any |= Padded(ib.FirstValue) | Padded(ib.StdDev);

        if (value is DoubleBarValue db)
            any |= Padded(db.FirstValue) | Padded(db.StdDev);

        return any;
    }

    // Nullable<T> is { bool hasValue; T value; }: the bytes between hasValue and value are padding, and a null
    // value still carries its value field. Any non-zero byte there is invisible to the value semantics.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
    private static bool Padded<T>(T? value) where T : unmanaged
    {
        var copy = value;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
            ref System.Runtime.CompilerServices.Unsafe.As<T?, byte>(ref copy), System.Runtime.CompilerServices.Unsafe.SizeOf<T?>());
        int valueOffset = bytes.Length - System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        for (int i = 1; i < bytes.Length; i++)
            if (bytes[i] != 0 && (i < valueOffset || !value.HasValue))
                return true;

        return false;
    }
}


internal static class ShardBench
{
    public static void Run(Options options)
    {
        Report.Line($"== Shard {Path.GetFileName(Path.GetFullPath(options.ShardPath).TrimEnd('\\', '/'))}");

        var sensors = new Dictionary<Guid, List<HistoryRow>>();
        long rows = 0, keyBytes = 0, valueBytes = 0, otherKeys = 0, unstableBlobs = 0;

        var load = Stopwatch.StartNew();
        using (var db = LevelDBDatabaseAdapter.ForReadOnly(Path.GetFullPath(options.ShardPath)))
        {
            foreach (var (key, value) in db.GetAll())
            {
                if (key.Length != 24)
                {
                    otherKeys++;
                    continue;
                }

                var dbKey = DbKey.FromBytes(key);
                var baseValue = MemoryPackSerializer.Deserialize<BaseValue>(value);

                if (!MemoryPackSerializer.Serialize(baseValue).AsSpan().SequenceEqual(value))
                    unstableBlobs++;

                if (!sensors.TryGetValue(dbKey.SensorId, out var list))
                    sensors[dbKey.SensorId] = list = new List<HistoryRow>();

                list.Add(new HistoryRow(dbKey.Timestamp, baseValue));
                rows++;
                keyBytes += key.Length;
                valueBytes += value.Length;
            }
        }

        foreach (var list in sensors.Values)
            list.Sort((a, b) => a.Key.CompareTo(b.Key));

        Report.Line($"rows {rows:N0}, sensors {sensors.Count:N0}, other keys {otherKeys}, load {load.Elapsed.TotalSeconds:F1} s");
        Report.Line($"source: keys {keyBytes:N0} B + MemoryPack values {valueBytes:N0} B = {(double)(keyBytes + valueBytes) / rows:F1} B/row before LevelDB");
        Report.Line($"MemoryPack blobs that do not re-serialize to the same bytes (not a codec issue): {unstableBlobs:N0}");

        foreach (var limit in options.Limits)
            RunAtLimit(sensors, limit, options, limit == options.Limits[0]);
    }


    private static void RunAtLimit(Dictionary<Guid, List<HistoryRow>> sensors, int limit, Options options, bool detailed)
    {
        Report.Line();
        Report.Line($"-- row limit {limit:N0}");

        var perType = new SortedDictionary<SensorType, (long Rows, long Bytes, long Payload)>();
        var refusals = new Dictionary<ChunkRefusalReason, (int Sensors, long Rows)>();
        var chunksBySensor = new Dictionary<Guid, IReadOnlyList<byte[]>>();
        long sealedRows = 0, chunkBytes = 0, chunkCount = 0, otherMismatches = 0, receivingMax = 0, zstdChunks = 0;
        long byteDifferences = 0, byteDifferencesFromPadding = 0;
        var mismatchFields = new Dictionary<string, long>();
        double encodeMs = 0, decodeMs = 0;

        foreach (var (id, list) in sensors)
        {
            var sw = Stopwatch.StartNew();
            var result = ChunkCodec.Encode(list, limit);
            encodeMs += sw.Elapsed.TotalMilliseconds;

            if (!result.IsSuccess)
            {
                var r = refusals.GetValueOrDefault(result.Refusal.Reason);
                refusals[result.Refusal.Reason] = (r.Sensors + 1, r.Rows + list.Count);
                if (detailed && result.Refusal.Reason != ChunkRefusalReason.UnsupportedValueType)
                    Report.Line($"   refused sensor {id}: {result.Refusal}");
                continue;
            }

            chunksBySensor[id] = result.Chunks;

            sw.Restart();
            var decoded = new List<HistoryRow>(list.Count);
            foreach (var chunk in result.Chunks)
                decoded.AddRange(ChunkCodec.Decode(chunk, limit));
            decodeMs += sw.Elapsed.TotalMilliseconds;

            if (decoded.Count != list.Count)
                throw new InvalidOperationException($"sensor {id}: {decoded.Count} rows decoded, {list.Count} encoded");

            for (int i = 0; i < list.Count; i++)
            {
                var difference = RowFidelity.FindDifference(list[i], decoded[i]);
                if (difference is not null)
                {
                    otherMismatches++;
                    mismatchFields[difference] = mismatchFields.GetValueOrDefault(difference) + 1;
                }

                if (!Compare.SameMemoryPackBytes(list[i], decoded[i]))
                {
                    byteDifferences++;
                    if (Compare.HasNullablePadding(list[i].Value))
                        byteDifferencesFromPadding++;
                }

                receivingMax = Math.Max(receivingMax, Math.Abs(list[i].Value.ReceivingTime.Ticks - decoded[i].Value.ReceivingTime.Ticks));
            }

            long bytes = result.Chunks.Sum(c => (long)c.Length);
            long payload = result.Chunks.Sum(c => (long)BitConverter.ToInt32(c, 12));
            zstdChunks += result.Chunks.Count(c => c[4] == 1);

            var type = list[0].Value.Type;
            var t = perType.GetValueOrDefault(type);
            perType[type] = (t.Rows + list.Count, t.Bytes + bytes, t.Payload + payload);

            sealedRows += list.Count;
            chunkBytes += bytes;
            chunkCount += result.Chunks.Count;
        }

        long allRows = sensors.Values.Sum(l => (long)l.Count);

        Report.Line($"sealed rows {sealedRows:N0} of {allRows:N0} in {chunkCount:N0} chunks ({zstdChunks:N0} zstd), chunk bytes {chunkBytes:N0}");
        Report.Line($"bytes per sealed row (stored chunk incl. 40 B header): {(double)chunkBytes / sealedRows:F2}");
        foreach (var (reason, (n, r)) in refusals)
            Report.Line($"refused: {reason}: {n} sensors, {r:N0} rows");
        Report.Line($"encode {encodeMs:F0} ms ({encodeMs * 1000 / sealedRows:F2} us/row), decode {decodeMs:F0} ms ({decodeMs * 1000 / sealedRows:F3} us/row)");
        Report.Line($"mismatches (RowFidelity, every field; ReceivingTime against its stored form): {otherMismatches} {string.Join(", ", mismatchFields.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Report.Line($"ReceivingTime max abs error: {receivingMax} ticks");
        Report.Line($"rows whose MemoryPack bytes differ from the source (ReceivingTime in stored form): {byteDifferences:N0}, of them with non-zero Nullable padding in the source: {byteDifferencesFromPadding:N0}");

        if (detailed)
        {
            Report.Line("per type: rows, stored B/row, bits/row, payload before zstd B/row");
            foreach (var (type, (r, b, p)) in perType)
                Report.Line($"   {type,-10} {r,10:N0} {(double)b / r,8:F2} {b * 8.0 / r,8:F1} {(double)p / r,8:F2}");

            var dense = sensors.Where(kv => chunksBySensor.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value.Count).First();
            var denseChunks = chunksBySensor[dense.Key];
            double ms = Report.Time(() =>
            {
                foreach (var c in denseChunks)
                    ChunkCodec.Decode(c, limit);
            }, 50);
            Report.Line($"densest sensor: {dense.Value.Count:N0} {dense.Value[0].Value.Type} rows in {denseChunks.Count} chunk(s), {denseChunks.Sum(c => c.Length):N0} B; decode of the week: {ms:F3} ms (median of 50)");

            if (options.OutPath is not null)
                WriteLevelDb(chunksBySensor, options.OutPath, sealedRows);
        }
    }

    private static void WriteLevelDb(Dictionary<Guid, IReadOnlyList<byte[]>> chunks, string outPath, long sealedRows)
    {
        var dir = Path.GetFullPath(outPath);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);

        using (var db = new LevelDBDatabaseAdapter(dir))
        {
            foreach (var (id, list) in chunks)
                foreach (var chunk in list)
                    db.Put(new DbKey(id, BitConverter.ToInt64(chunk, 20)).ToBytes(), chunk);

            db.Compact();
        }

        long size = Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);
        Report.Line($"LevelDB with the chunks after Compact(): {size:N0} B = {(double)size / sealedRows:F2} B per sealed row");
    }
}


internal static class SyntheticBench
{
    public static void Run(Options options)
    {
        var limits = options.Limits.Length > 1 ? options.Limits : [1024, 2048, 4096, 8192, 16384, 32768, 65536];

        foreach (var (name, rows) in new[] { ("Double, 2 decimals", Dense(SensorType.Double)), ("Rate, computed ratios", Dense(SensorType.Rate)) })
        {
            Report.Line();
            Report.Line($"== Synthetic dense sensor: {name}, {rows.Count:N0} rows (one per second for a week)");
            Report.Line("limit      chunks   B/row   encode ms   decode week ms   decode one chunk ms   mismatches");

            foreach (var limit in limits)
            {
                ChunkEncodeResult result = null;
                double encodeMs = Report.Time(() => result = ChunkCodec.Encode(rows, limit), 3);
                var chunks = result.Chunks;
                double decodeWeekMs = Report.Time(() => { foreach (var c in chunks) ChunkCodec.Decode(c, limit); }, 9);
                double decodeOneMs = Report.Time(() => ChunkCodec.Decode(chunks[chunks.Count / 2], limit), 31);

                var decoded = chunks.SelectMany(c => ChunkCodec.Decode(c, limit)).ToList();
                int mismatches = 0;
                for (int i = 0; i < rows.Count; i++)
                    if (RowFidelity.FindDifference(rows[i], decoded[i]) is not null || !Compare.SameMemoryPackBytes(rows[i], decoded[i]) ||
                        Math.Abs(rows[i].Value.ReceivingTime.Ticks - decoded[i].Value.ReceivingTime.Ticks) > 5000)
                        mismatches++;

                double bytesPerRow = (double)chunks.Sum(c => (long)c.Length) / rows.Count;
                Report.Line($"{limit,-8} {chunks.Count,8} {bytesPerRow,7:F2} {encodeMs,11:F0} {decodeWeekMs,16:F1} {decodeOneMs,21:F3} {mismatches,12}");
            }
        }
    }

    private static List<HistoryRow> Dense(SensorType type)
    {
        var random = new Random(1802);
        var rows = new List<HistoryRow>();
        var time = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        double level = 50;

        for (int i = 0; i < 7 * 24 * 3600; i++)
        {
            time = time.AddTicks(TimeSpan.TicksPerSecond + random.Next(-200_000, 200_000));
            level = Math.Round(level + random.Next(-100, 101) / 100.0, 2);

            BaseValue value = type == SensorType.Double
                ? new DoubleValue { Value = level, EmaValue = i % 60 == 0 ? null : Math.Round(level * 0.97, 2) }
                : new RateValue { Value = random.Next(0, 1000) / (random.NextDouble() * 60 + 1), EmaValue = null };

            value = value with { ReceivingTime = time.AddTicks(random.Next(500_000, 3_000_000)) };
            value.Time = time;
            rows.Add(new HistoryRow(time.Ticks, value));
        }

        return rows;
    }
}

