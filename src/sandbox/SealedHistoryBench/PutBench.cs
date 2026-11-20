using System.Diagnostics;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.Sealed;

// The cost of the week handle on the write path (#1804 review S5): PutSensorValue on a current week through
// SensorValuesWeek (a shared ReaderWriterLockSlim per write) against the row worker directly, 1 and 8 writer threads.
//
//   SealedHistoryBench --put-bench <work-dir>
internal static class PutBench
{
    private const int Writes = 400_000;


    public static void Run(Options options)
    {
        var formatter = new MemoryPackFormatter();
        var from = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var to = from + TimeSpan.TicksPerDay * 7;
        var value = formatter.Serialize(new DoubleValue { Value = 1.5, Time = new DateTime(from, DateTimeKind.Utc), ReceivingTime = new DateTime(from, DateTimeKind.Utc) });

        foreach (var threads in new[] { 1, 8 })
            for (int round = 0; round < 3; round++)
            {
                var direct = Measure(options.PutWorkPath, "direct", threads, from, to, value,
                    (path, sealedPath) => LevelDBManager.GetSensorValuesDatabaseInstance(path, from, to));
                var handle = Measure(options.PutWorkPath, "handle", threads, from, to, value,
                    (path, sealedPath) => new SensorValuesWeek(path, sealedPath, from, to));

                Report.Line($"{threads} thread(s), round {round}: worker {direct / 1000:F0} k puts/s, week handle {handle / 1000:F0} k puts/s ({(handle / direct - 1) * 100:+0.0;-0.0} %)");
            }
    }

    private static double Measure(string root, string name, int threads, long from, long to, byte[] value, Func<string, string, ISensorValuesDatabase> open)
    {
        var folder = Path.Combine(root, $"{name}-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, $"SensorValuesV2_{from}_{to}");
        var sealedPath = Path.Combine(folder, $"{SealedWeekMarker.DirectoryPrefix}_{from}_{to}");

        using (var db = open(path, sealedPath))
        {
            var sensors = Enumerable.Range(0, threads).Select(_ => Guid.NewGuid()).ToArray();
            var watch = Stopwatch.StartNew();

            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                for (int i = 0; i < Writes / threads; i++)
                    db.PutSensorValue(new DbKey(sensors[t], from + i * TimeSpan.TicksPerSecond).ToBytes(), value);
            });

            var rate = Writes / watch.Elapsed.TotalSeconds;

            db.Dispose();
            Directory.Delete(folder, recursive: true);

            return rate;
        }
    }
}
