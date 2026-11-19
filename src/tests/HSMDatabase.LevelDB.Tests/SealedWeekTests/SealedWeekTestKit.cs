using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.SealedHistory;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

/// <summary>One chunk as the sealer wrote it.</summary>
internal readonly record struct SealedChunk(Guid SensorId, long FirstTick, long LastTick, int Rows);


internal sealed class SealResult
{
    public List<SealedChunk> Chunks { get; } = [];

    /// <summary>The MemoryPack bytes of every row that went into a chunk, as hex: reads may return these by the fidelity rule.</summary>
    public HashSet<string> SealedOriginals { get; } = [];

    public int SealedRows { get; set; }

    public int RefusedSensors { get; set; }
}


/// <summary>
/// The test-only sealer (#1803; the real one is #1804): encodes the rows of every sensor of a closed week into
/// chunks, verifies them field by field, writes the sibling chunk database and its completion marker, and then
/// deletes the sealed rows from the row database (unless asked to keep them, as during the keep period).
/// Sensors the codec refuses (File values) keep their rows. The week must not be open.
/// </summary>
internal static class WeekSealer
{
    private static readonly MemoryPackFormatter _formatter = new();


    public static SealResult Seal(string rowPath, string sealedPath, bool keepSourceRows = false, string skipMarker = null)
    {
        var result = new SealResult();
        var sealedKeys = new List<byte[]>();

        using (var rows = new LevelDBDatabaseAdapter(rowPath))
        {
            var writer = SealedWeekWriter.Open(sealedPath);

            try
            {
                foreach (var (sensorId, sensorRows) in BySensor(rows.GetAll()))
                {
                    var history = sensorRows.Select(r => new HistoryRow(DbKey.FromBytes(r.key).Timestamp, _formatter.Deserialize(r.value))).ToList();
                    var encoded = ChunkCodec.Encode(history);

                    if (!encoded.IsSuccess)
                    {
                        result.RefusedSensors++;
                        continue;
                    }

                    var decoded = encoded.Chunks.SelectMany(c => ChunkCodec.Decode(c)).ToList();
                    Assert.Equal(history.Count, decoded.Count);
                    for (int i = 0; i < history.Count; i++)
                        Assert.Null(RowFidelity.FindDifference(history[i], decoded[i]));

                    writer.WriteChunks(sensorId, encoded.Chunks);

                    foreach (var chunk in encoded.Chunks)
                    {
                        var header = ChunkCodec.ReadHeader(chunk);
                        result.Chunks.Add(new SealedChunk(sensorId, header.FirstKey, header.LastKey, header.RowCount));
                    }

                    foreach (var (key, value) in sensorRows)
                    {
                        sealedKeys.Add(key);
                        result.SealedOriginals.Add(Convert.ToHexString(value));
                    }

                    result.SealedRows += history.Count;
                }

                if (skipMarker is null)
                    writer.Complete($"test seal: {result.SealedRows} rows, {result.Chunks.Count} chunks");
            }
            finally
            {
                writer.Dispose();
            }

            if (!keepSourceRows && skipMarker is null)
                rows.PutBatch([], sealedKeys);
        }

        return result;
    }

    private static IEnumerable<(Guid, List<(byte[] key, byte[] value)>)> BySensor(IEnumerable<(byte[], byte[])> rows)
    {
        List<(byte[], byte[])> current = null;
        Guid currentId = default;

        foreach (var (key, value) in rows)
        {
            var id = DbKey.FromBytes(key).SensorId;

            if (current is not null && id != currentId)
            {
                yield return (currentId, current);
                current = null;
            }

            current ??= [];
            currentId = id;
            current.Add((key, value));
        }

        if (current is not null)
            yield return (currentId, current);
    }
}


/// <summary>Values as the server stores them: <c>Time</c> is the key's tick (UTC); a unique comment per value.</summary>
internal static class TestValues
{
    public static readonly SensorType[] SealedTypes =
    [
        SensorType.Boolean, SensorType.Integer, SensorType.Double, SensorType.String, SensorType.IntegerBar,
        SensorType.DoubleBar, SensorType.TimeSpan, SensorType.Version, SensorType.Rate, SensorType.Enum,
    ];

    private static readonly MemoryPackFormatter _formatter = new();
    private static int _serial;


    public static byte[] Bytes(SensorType type, long tick, Random random) => _formatter.Serialize(Make(type, tick, random));

    public static BaseValue Make(SensorType type, long tick, Random random)
    {
        var serial = Interlocked.Increment(ref _serial);
        var level = Math.Round(random.NextDouble() * 100, 2);
        var time = new DateTime(tick, DateTimeKind.Utc);

        BaseValue value = type switch
        {
            SensorType.Boolean => new BooleanValue { Value = random.Next(2) == 0 },
            SensorType.Integer => new IntegerValue { Value = random.Next(-1000, 1000) },
            SensorType.Double => new DoubleValue { Value = level, EmaValue = random.Next(3) == 0 ? null : level * 0.9 },
            SensorType.Rate => new RateValue { Value = random.NextDouble() * 10 },
            SensorType.String => new StringValue { Value = $"state {random.Next(7)}" },
            SensorType.TimeSpan => new TimeSpanValue { Value = TimeSpan.FromMilliseconds(random.Next(100_000)) },
            SensorType.Version => new VersionValue { Value = random.Next(5) == 0 ? null : new Version(1, 2, random.Next(3), 4) },
            SensorType.Enum => new EnumValue { Value = random.Next(4) },
            SensorType.IntegerBar => new IntegerBarValue
            {
                Count = 60, OpenTime = time.AddMinutes(-1), CloseTime = time, Min = -5, Max = 5, Mean = 0,
                FirstValue = random.Next(4) == 0 ? null : 1, LastValue = 2, StdDev = random.Next(3) == 0 ? null : 1.25, EmaMean = level,
            },
            SensorType.DoubleBar => new DoubleBarValue
            {
                Count = 60, OpenTime = time.AddMinutes(-1), CloseTime = time, Min = level - 1, Max = level + 1, Mean = level,
                FirstValue = random.Next(4) == 0 ? null : level, LastValue = level, StdDev = 0.5,
            },
            SensorType.File => new FileValue { Value = [1, 2, 3, (byte)random.Next(256)], Name = "report", Extension = "txt", OriginalSize = 4 },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        // A sub-millisecond ReceivingTime: a sealed row then reads back with different bytes, equal by the fidelity rule.
        value = value with
        {
            ReceivingTime = time.AddTicks(random.Next(0, 30_000_000)),
            Comment = $"#{serial}",
            Status = random.Next(10) == 0 ? SensorStatus.Error : SensorStatus.Ok,
        };
        value.Time = time;

        return value;
    }
}


/// <summary>
/// How a sealed read is compared with a plain one: identical bytes, or, for a value whose plain bytes are a row
/// that was sealed (or a copy of one), equal by <see cref="RowFidelity"/>. A row that was never sealed must
/// come back byte for byte.
/// </summary>
internal sealed class FidelityComparer(HashSet<string> sealedOriginals)
{
    private static readonly MemoryPackFormatter _formatter = new();

    public int FidelityMatches { get; private set; }


    public void Value(byte[] plain, byte[] sealedValue, string what)
    {
        if (plain is null || sealedValue is null)
        {
            Assert.True(plain is null && sealedValue is null, $"{what}: plain {(plain is null ? "null" : "value")}, sealed {(sealedValue is null ? "null" : "value")}");
            return;
        }

        if (plain.AsSpan().SequenceEqual(sealedValue))
            return;

        Assert.True(sealedOriginals.Contains(Convert.ToHexString(plain)), $"{what}: an unsealed row came back with different bytes");

        var p = _formatter.Deserialize(plain);
        var s = _formatter.Deserialize(sealedValue);
        var difference = RowFidelity.FindDifference(new HistoryRow(p.Time.Ticks, p), new HistoryRow(p.Time.Ticks, s));

        Assert.True(difference is null, $"{what}: field {difference} differs");
        FidelityMatches++;
    }

    public void Values(IEnumerable<byte[]> plain, IEnumerable<byte[]> sealedValues, string what)
    {
        var p = plain.ToList();
        var s = sealedValues.ToList();

        Assert.True(p.Count == s.Count, $"{what}: {p.Count} plain values, {s.Count} sealed");

        for (int i = 0; i < p.Count; i++)
            Value(p[i], s[i], $"{what}[{i}]");
    }

    public void Pairs(IEnumerable<(byte[] key, byte[] value)> plain, IEnumerable<(byte[] key, byte[] value)> sealedPairs, string what)
    {
        var p = plain.ToList();
        var s = sealedPairs.ToList();

        Assert.True(p.Count == s.Count, $"{what}: {p.Count} plain rows, {s.Count} sealed");

        for (int i = 0; i < p.Count; i++)
        {
            Assert.True(p[i].key.AsSpan().SequenceEqual(s[i].key), $"{what}[{i}]: key {DbKey.FromBytes(p[i].key)} vs {DbKey.FromBytes(s[i].key)}");
            Value(p[i].value, s[i].value, $"{what}[{i}]");
        }
    }
}


/// <summary>
/// The same week twice: a plain row database, and a sealed week built from the same rows. Every operation is
/// applied to both; <see cref="AssertSameReads"/> runs every read of <see cref="ISensorValuesDatabase"/> on both.
/// </summary>
internal sealed class WeekPair : IDisposable
{
    public static readonly long WeekFrom = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
    public static readonly long WeekTo = WeekFrom + TimeSpan.TicksPerDay * 7;

    private readonly string _root;

    public ISensorValuesDatabase Plain { get; private set; }

    public ISensorValuesDatabase Sealed { get; private set; }

    public SealedSensorValuesDatabase SealedStore => (SealedSensorValuesDatabase)Sealed;

    public SealResult Seal { get; private set; }

    public FidelityComparer Compare { get; private set; }

    public string SealedRowPath => Path.Combine(_root, "sealed", $"SensorValuesV2_{WeekFrom}_{WeekTo}");

    public string SealedChunkPath => Path.Combine(_root, "sealed", $"{SealedWeekMarker.DirectoryPrefix}_{WeekFrom}_{WeekTo}");

    private string PlainRowPath => Path.Combine(_root, "plain", $"SensorValuesV2_{WeekFrom}_{WeekTo}");

    private string PlainChunkPath => Path.Combine(_root, "plain", $"{SealedWeekMarker.DirectoryPrefix}_{WeekFrom}_{WeekTo}");


    private WeekPair()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hsm-1803-{Guid.NewGuid():N}");
    }


    /// <summary>Writes the seed into both weeks, seals one of them and opens both through LevelDBManager.</summary>
    public static WeekPair Create(IReadOnlyList<(byte[] key, byte[] value)> seed, bool keepSourceRows = false, Action<WeekPair> beforeOpen = null)
    {
        var pair = new WeekPair();

        try
        {
            using (var rows = new LevelDBDatabaseAdapter(pair.SealedRowPath))
                foreach (var (key, value) in seed)
                    rows.Put(key, value);

            pair.Seal = WeekSealer.Seal(pair.SealedRowPath, pair.SealedChunkPath, keepSourceRows);
            beforeOpen?.Invoke(pair);

            pair.Plain = LevelDBManager.GetSensorValuesDatabaseInstance(pair.PlainRowPath, pair.PlainChunkPath, WeekFrom, WeekTo);
            foreach (var (key, value) in seed)
                pair.Plain.PutSensorValue(key, value);

            pair.Sealed = LevelDBManager.GetSensorValuesDatabaseInstance(pair.SealedRowPath, pair.SealedChunkPath, WeekFrom, WeekTo);
            Assert.IsType<SensorValuesDatabaseWorker>(pair.Plain);
            Assert.IsType<SealedSensorValuesDatabase>(pair.Sealed);

            pair.Compare = new FidelityComparer(pair.Seal.SealedOriginals);

            return pair;
        }
        catch
        {
            pair.Dispose();
            throw;
        }
    }

    public void Reopen()
    {
        Sealed.Dispose();
        Sealed = LevelDBManager.GetSensorValuesDatabaseInstance(SealedRowPath, SealedChunkPath, WeekFrom, WeekTo);
    }

    /// <summary>Closes the sealed week, so a test can look at its directories directly.</summary>
    public void CloseSealed()
    {
        Sealed.Dispose();
        Sealed = null;
    }

    public void Put(byte[] key, byte[] value)
    {
        Plain.PutSensorValue(key, value);
        Sealed.PutSensorValue(key, value);
    }

    public void Remove(byte[] from, byte[] to)
    {
        Plain.RemoveSensorValues(from, to);
        Sealed.RemoveSensorValues(from, to);
    }

    /// <summary>DatabaseCore.MergeSensorValues over one week, applied to both.</summary>
    public void Merge(Guid fromSensor, Guid intoSensor)
    {
        var plain = MergeWeek(Plain, fromSensor, intoSensor);
        var sealedResult = MergeWeek(Sealed, fromSensor, intoSensor);

        Assert.Equal(plain, sealedResult);
    }

    // The loop of DatabaseCore.MergeSensorValues for one partition.
    public static (long copied, long collisions) MergeWeek(ISensorValuesDatabase db, Guid fromSensorId, Guid intoSensorId)
    {
        var fromStart = new DbKey(fromSensorId, DateTime.MinValue.Ticks).ToBytes();
        var fromEnd = new DbKey(fromSensorId, DateTime.MaxValue.Ticks).ToBytes();
        var intoStart = new DbKey(intoSensorId, DateTime.MinValue.Ticks).ToBytes();
        var intoEnd = new DbKey(intoSensorId, DateTime.MaxValue.Ticks).ToBytes();

        long copied = 0, collisions = 0;
        IEnumerator<long> into = null;
        var hasInto = false;

        try
        {
            foreach (var (keyBytes, valueBytes) in db.GetKeysValuesTo(fromStart, fromEnd))
            {
                if (into is null)
                {
                    into = db.GetKeysValuesTo(intoStart, intoEnd).Select(kv => DbKey.FromBytes(kv.key).Timestamp).GetEnumerator();
                    hasInto = into.MoveNext();
                }

                var tick = DbKey.FromBytes(keyBytes).Timestamp;

                while (hasInto && into.Current > tick)
                    hasInto = into.MoveNext();

                if (hasInto && into.Current == tick)
                {
                    collisions++;
                    continue;
                }

                db.PutSensorValueOrThrow(new DbKey(intoSensorId, tick).ToBytes(), valueBytes);
                copied++;
            }
        }
        finally
        {
            into?.Dispose();
        }

        return (copied, collisions);
    }

    /// <summary>Every read of the interface on both weeks, over the probe keys and ranges.</summary>
    public void AssertSameReads(IReadOnlyList<Guid> sensors, Random random, int probesPerSensor = 12)
    {
        var all = Plain.GetAll().ToList();
        Compare.Pairs(all, Sealed.GetAll(), "GetAll");

        var probes = ProbeKeys(sensors, all, random, probesPerSensor);

        foreach (var key in probes)
        {
            var prefix = key.AsSpan(0, 16).ToArray();
            var at = DbKey.FromBytes(key).ToString();

            Compare.Value(Plain.Get(key, prefix), Sealed.Get(key, prefix), $"Get {at}");
            Compare.Value(Plain.GetLatest(key, prefix), Sealed.GetLatest(key, prefix), $"GetLatest {at}");
            Compare.Value(Plain.GetFirst(key, prefix), Sealed.GetFirst(key, prefix), $"GetFirst {at}");
        }

        foreach (var (from, to) in ProbeRanges(sensors, probes, random))
        {
            var range = $"[{DbKey.FromBytes(from)}, {DbKey.FromBytes(to)}]";

            Compare.Values(Plain.GetValuesFrom(from, to), Sealed.GetValuesFrom(from, to), $"GetValuesFrom {range}");
            Compare.Values(Plain.GetValuesTo(from, to), Sealed.GetValuesTo(from, to), $"GetValuesTo {range}");
            Compare.Pairs(Plain.GetKeysValuesTo(from, to), Sealed.GetKeysValuesTo(from, to), $"GetKeysValuesTo {range}");

            // A page read stops early: the cursor is disposed in the middle of a chunk.
            foreach (var count in new[] { 1, 7, 100 })
            {
                Compare.Values(Plain.GetValuesFrom(from, to).Take(count), Sealed.GetValuesFrom(from, to).Take(count), $"GetValuesFrom {range} x{count}");
                Compare.Values(Plain.GetValuesTo(from, to).Take(count), Sealed.GetValuesTo(from, to).Take(count), $"GetValuesTo {range} x{count}");
            }

            Assert.Equal(Plain.CalculateHistorySize(from, to).count, Sealed.CalculateHistorySize(from, to).count);
        }

        var ids = sensors.Append(Guid.NewGuid()).ToList();
        AssertSameFirstLast(Plain.GetLastAndFirstValues(ids), Sealed.GetLastAndFirstValues(ids), "GetLastAndFirstValues");

        var prefilled = () => ids.Where((_, i) => i % 2 == 0).ToDictionary(id => id, id => ((byte[])[9, 9], (byte[])null));
        AssertSameFirstLast(Plain.GetLastAndFirstValues(ids, prefilled()), Sealed.GetLastAndFirstValues(ids, prefilled()), "GetLastAndFirstValues prefilled");

        foreach (var toTick in new[] { WeekFrom, WeekFrom + random.NextInt64(0, WeekTo - WeekFrom), WeekTo, DateTime.MaxValue.Ticks })
            foreach (var from in new[] { 0L, WeekTo + 1 })
            {
                var plainFill = FillRequest(ids, from, toTick);
                var sealedFill = FillRequest(ids, from, toTick);

                Plain.FillLatestValues(plainFill);
                Sealed.FillLatestValues(sealedFill);

                foreach (var (key, (_, _, value)) in plainFill)
                    Compare.Value(value, sealedFill.Single(kv => kv.Key.AsSpan().SequenceEqual(key)).Value.Item3, $"FillLatestValues {new Guid(key)} to {toTick}");
            }
    }

    private void AssertSameFirstLast(Dictionary<Guid, (byte[], byte[])> plain, Dictionary<Guid, (byte[], byte[])> sealedResult, string what)
    {
        Assert.Equal(plain.Keys.OrderBy(k => k), sealedResult.Keys.OrderBy(k => k));

        foreach (var (id, (first, last)) in plain)
        {
            Compare.Value(first, sealedResult[id].Item1, $"{what} first {id}");
            Compare.Value(last, sealedResult[id].Item2, $"{what} last {id}");
        }
    }

    private static Dictionary<byte[], (long, byte[], byte[])> FillRequest(IEnumerable<Guid> ids, long from, long toTick) =>
        ids.ToDictionary(id => new DbKey(id, 0).ToPrefixBytes(), id => (from, new DbKey(id, toTick).ToBytes(), (byte[])null));

    private static List<byte[]> ProbeKeys(IReadOnlyList<Guid> sensors, List<(byte[] key, byte[] value)> all, Random random, int perSensor)
    {
        var keys = new List<byte[]>();

        foreach (var sensor in sensors)
        {
            var own = all.Where(r => DbKey.FromBytes(r.key).SensorId == sensor).Select(r => DbKey.FromBytes(r.key).Timestamp).ToList();

            keys.Add(new DbKey(sensor, 0).ToBytes());
            keys.Add(new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes());
            keys.Add(new DbKey(sensor, WeekFrom).ToBytes());
            keys.Add(new DbKey(sensor, WeekTo).ToBytes());

            if (own.Count == 0)
                continue;

            var picks = new List<long> { own[0], own[^1] };
            for (int i = 0; i < perSensor; i++)
                picks.Add(own[random.Next(own.Count)]);

            foreach (var tick in picks)
                foreach (var delta in new[] { -1L, 0L, 1L })
                    keys.Add(new DbKey(sensor, tick + delta).ToBytes());
        }

        return keys;
    }

    private IEnumerable<(byte[] from, byte[] to)> ProbeRanges(IReadOnlyList<Guid> sensors, List<byte[]> probes, Random random)
    {
        foreach (var sensor in sensors)
            yield return (new DbKey(sensor, 0).ToBytes(), new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes());

        // Across the borders of the chunks as sealed.
        foreach (var chunk in Seal.Chunks.Where((_, i) => i % 2 == 0))
        {
            yield return (new DbKey(chunk.SensorId, chunk.FirstTick).ToBytes(), new DbKey(chunk.SensorId, chunk.LastTick).ToBytes());
            yield return (new DbKey(chunk.SensorId, chunk.LastTick - 5 * TimeSpan.TicksPerSecond).ToBytes(), new DbKey(chunk.SensorId, chunk.LastTick + 5 * TimeSpan.TicksPerSecond).ToBytes());
        }

        // Across sensors, and empty or reversed ranges.
        var sorted = sensors.Select(s => new DbKey(s, 0).ToBytes()).OrderBy(k => k, ByteOrder.Instance).ToList();
        yield return (sorted[0], new DbKey(new Guid(sorted[^1].AsSpan(0, 16)), DateTime.MaxValue.Ticks).ToBytes());

        for (int i = 0; i < 20; i++)
        {
            var a = probes[random.Next(probes.Count)];
            var b = probes[random.Next(probes.Count)];
            yield return (a, b);
        }
    }

    public void Dispose()
    {
        Plain?.Dispose();
        Sealed?.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}


internal sealed class ByteOrder : IComparer<byte[]>
{
    public static readonly ByteOrder Instance = new();

    public int Compare(byte[] x, byte[] y) => x.AsSpan().SequenceCompareTo(y);
}
