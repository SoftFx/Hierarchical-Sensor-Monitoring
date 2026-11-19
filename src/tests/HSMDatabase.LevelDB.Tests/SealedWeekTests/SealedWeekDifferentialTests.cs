using HSMCommon.Model;
using HSMDatabase.AccessManager;
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
    [InlineData(false)]
    [InlineData(true)] // the keep period: the overlay still holds every sealed row
    public void ScriptedOperations_GiveTheSameReads(bool keepSourceRows)
    {
        var random = new Random(1803);
        var (seed, sensors) = Seed(random);

        using var pair = WeekPair.Create(seed, keepSourceRows);

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
        var target = Guid.NewGuid();
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
    }

    [Fact]
    public void RandomOperations_GiveTheSameReads()
    {
        const int seed = 18030;
        const int steps = 400;

        output.WriteLine($"seed {seed}");

        var random = new Random(seed);
        var (data, sensors) = Seed(random);
        var extra = Guid.NewGuid();
        var all = sensors.All.Append(extra).ToList();
        var types = new Dictionary<Guid, SensorType>(sensors.Types) { [extra] = SensorType.Integer };

        using var pair = WeekPair.Create(data);

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
            else if (op < 55)
            {
                var ticks = Ticks(pair, sensor);
                if (ticks.Count == 0)
                    continue;

                var tick = ticks[random.Next(ticks.Count)];
                pair.Put(Key(sensor, tick), TestValues.Bytes(types[sensor], tick, random));
                what = $"rewrite {sensor} {tick}";
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
                output.WriteLine($"step {step}: {what}; checking");
                pair.AssertSameReads(all, random, probesPerSensor: 4);
            }
        }

        pair.Reopen();
        pair.AssertSameReads(all, random);
        output.WriteLine($"fidelity matches: {pair.Compare.FidelityMatches}");
        Assert.True(pair.Compare.FidelityMatches > 0);
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

    private static void Remove(WeekPair pair, Guid sensor, long from, long to) => pair.Remove(Key(sensor, from), Key(sensor, to));

    private static List<long> Ticks(WeekPair pair, Guid sensor) =>
        pair.Plain.GetKeysValuesTo(new DbKey(sensor, 0).ToBytes(), new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes())
            .Select(kv => DbKey.FromBytes(kv.key).Timestamp).Reverse().ToList();

    private static long Mid(WeekPair pair, Guid sensor)
    {
        var ticks = Ticks(pair, sensor);
        return ticks.Count == 0 ? WeekPair.WeekFrom : ticks[ticks.Count / 2];
    }

    // Whole sensors, ranges inside and across the chunks as sealed, across sensors, empty and reversed ones.
    private static (byte[] from, byte[] to) RandomRange(WeekPair pair, List<Guid> sensors, Random random)
    {
        var sensor = sensors[random.Next(sensors.Count)];

        switch (random.Next(6))
        {
            case 0:
                return (Key(sensor, 0), Key(sensor, DateTime.MaxValue.Ticks));

            case 1:
            case 2:
                {
                    var chunk = pair.Seal.Chunks[random.Next(pair.Seal.Chunks.Count)];
                    var span = Math.Max(1, chunk.LastTick - chunk.FirstTick);
                    var from = chunk.FirstTick + random.NextInt64(-span / 4, span);
                    var to = from + random.NextInt64(0, span / 2 + 1);
                    return (Key(chunk.SensorId, from), Key(chunk.SensorId, to));
                }

            case 3:
                {
                    var other = sensors[random.Next(sensors.Count)];
                    var a = Key(sensor, Mid(pair, sensor));
                    var b = Key(other, Mid(pair, other));
                    return ByteOrder.Instance.Compare(a, b) <= 0 ? (a, b) : (b, a);
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
