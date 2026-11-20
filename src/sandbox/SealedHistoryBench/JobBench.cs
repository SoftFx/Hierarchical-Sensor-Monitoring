using System.Diagnostics;
using HSMCommon.Model;
using HSMDatabase;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;

// The sealing job (#1804) through DatabaseCore, with an injected clock: time and peak memory of a seal, of the
// source-row removal, of a fold of a small overlay and of an unseal, and the bytes on disk at each stage.
//
//   SealedHistoryBench --job-synthetic <work-dir>              three synthetic weeks, 300 sensors
//   SealedHistoryBench --job-shard <shard-copy> <work-dir>     one copied weekly shard (its files are copied again into <work-dir>)
internal static class JobBench
{
    private static readonly HistorySealingSettings Settings = new()
    {
        Enabled = true,
        SealAfter = TimeSpan.FromDays(7),
        KeepSourceRows = TimeSpan.FromDays(28),
        FoldMinRows = 500,
        FoldQuietPeriod = TimeSpan.FromDays(1),
    };


    public static void Run(Options options)
    {
        var folder = Path.Combine(options.JobWorkPath, "db");

        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(folder);

        var settings = new DatabaseSettings { DatabaseFolder = folder };
        var random = new Random(1804);
        var weeks = new List<long>();

        // The shard is copied before the database starts: it lists the week directories once, at startup.
        if (options.JobShardPath is { } shard)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(shard));
            CopyDirectory(shard, Path.Combine(folder, name));
            weeks.Add(new DateTime(long.Parse(name.Split('_')[1])).Ticks);
        }

        var core = new DatabaseCore(settings);

        if (options.JobShardPath is null)
        {
            var start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

            for (int w = 0; w < 3; w++)
            {
                weeks.Add(start.AddDays(7 * w).Ticks);
                WriteSyntheticWeek(core, start.AddDays(7 * w), random);
            }
        }
        var now = new DateTime(weeks.Max(), DateTimeKind.Utc).AddDays(7 + 8);

        Report.Line($"weeks: {weeks.Count}; history on disk {Mb(core.SensorHistoryDbSize)}");
        Report.Line();

        core.Compact(); // a fair "before": the rows as compacted as they get without sealing
        Report.Line($"after a compaction of the rows: {Mb(core.SensorHistoryDbSize)}");

        foreach (var _ in weeks)
            Step(core, "seal", () => core.RunHistorySealingPass(now, Settings));

        Report.Line($"after sealing (source rows kept): {Mb(core.SensorHistoryDbSize)}");

        now += Settings.KeepSourceRows;

        foreach (var _ in weeks)
            Step(core, "remove source rows", () => core.RunHistorySealingPass(now, Settings));

        Report.Line($"after source-row removal: {Mb(core.SensorHistoryDbSize)}");
        Step(core, "compact (manual, all weeks)", () => { core.Compact(); return null; });
        Report.Line($"after compaction: {Mb(core.SensorHistoryDbSize)}");

        // A small overlay: 1,000 late values and edits spread over 20 sensors of the newest week.
        var week = core.SensorValuesDatabases.Last();
        var rows = week.GetAll().Select(kv => DbKey.FromBytes(kv.key)).ToList();
        var formatter = new HSMDatabase.AccessManager.Formatters.MemoryPackFormatter();
        var sensors = rows.Select(k => k.SensorId).Distinct().OrderBy(_ => random.Next()).Take(20).ToList();

        foreach (var sensor in sensors)
        {
            var own = rows.Where(k => k.SensorId == sensor).ToList();
            var sample = formatter.Deserialize(week.Get(new DbKey(sensor, own[0].Timestamp).ToBytes(), sensor.ToByteArray()));

            if (sample is FileValue)
                continue;

            for (int i = 0; i < 50; i++)
            {
                var tick = own[random.Next(own.Count)].Timestamp + (i % 2 == 0 ? 0 : 1);
                var value = sample with { Comment = $"late {i}", ReceivingTime = new DateTime(tick, DateTimeKind.Utc) };
                value.Time = new DateTime(tick, DateTimeKind.Utc);
                core.AddSensorValue(sensor, value);
            }
        }

        Step(core, "fold (counting pass)", () => core.RunHistorySealingPass(now, Settings));
        Step(core, "fold", () => core.RunHistorySealingPass(now, Settings));

        Step(core, "unseal (newest week)", () => core.UnsealHistoryWeek(week.Name, now));
        Report.Line($"after the unseal: {Mb(core.SensorHistoryDbSize)}");

        core.Dispose();
    }

    private static void Step(DatabaseCore core, string what, Func<HistorySealingPassReport> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        var baseHeap = GC.GetTotalMemory(false);
        var baseSet = process.WorkingSet64;
        long peakHeap = baseHeap, peakSet = baseSet;
        var stop = false;

        var sampler = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
                process.Refresh();
                peakSet = Math.Max(peakSet, process.WorkingSet64);
                Thread.Sleep(2);
            }
        });

        sampler.Start();
        var watch = Stopwatch.StartNew();
        var report = action();
        watch.Stop();
        Volatile.Write(ref stop, true);
        sampler.Join();

        Report.Line($"{what,-28} {watch.Elapsed.TotalSeconds,7:F2} s  heap +{Mb(peakHeap - baseHeap),-9} working set +{Mb(peakSet - baseSet),-9} {Describe(report)}");
    }

    private static string Describe(HistorySealingPassReport report) => report is null ? string.Empty
        : $"{report.Action} {report.Outcome}: {report.RowsSealed} rows sealed, {report.SourceBytes} -> {report.ChunkBytes} B, " +
          $"{report.OverlayRowsRemoved} overlay rows removed, {report.RowsRestored} restored, {report.SensorsSealed} sensors, left as rows [{report.LeftAsRowsSummary()}]" +
          string.Concat(report.LongestExclusiveHold.Select(h => $"; longest '{h.Key}' {h.Value.TotalMilliseconds:F2} ms"));

    // A dense Double sensor (one value per second for two days) and 299 sensors of every type, one value every 1 to 20 minutes.
    private static void WriteSyntheticWeek(DatabaseCore core, DateTime monday, Random random)
    {
        SensorType[] types = [SensorType.Boolean, SensorType.Integer, SensorType.Double, SensorType.String, SensorType.IntegerBar, SensorType.DoubleBar, SensorType.TimeSpan, SensorType.Version, SensorType.Rate, SensorType.Enum];

        var ids = new Random(1804); // the same sensors every week
        var from = monday.Ticks;
        var to = monday.AddDays(7).Ticks;

        void Sensor(SensorType type, long step, long until)
        {
            var bytes = new byte[16];
            ids.NextBytes(bytes);
            var id = new Guid(bytes);
            double level = 50;

            for (long tick = from + random.NextInt64(0, step); tick < until; tick += step + random.NextInt64(-step / 50, step / 50 + 1))
            {
                level = Math.Round(level + random.Next(-100, 101) / 100.0, 2);
                core.AddSensorValue(id, Value(type, tick, level, random));
            }
        }

        Sensor(SensorType.Double, TimeSpan.TicksPerSecond, from + 2 * TimeSpan.TicksPerDay);

        for (int i = 0; i < 299; i++)
            Sensor(i % 50 == 0 ? SensorType.File : types[i % types.Length], TimeSpan.TicksPerMinute * (1 + i % 20), to);
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

    private static string Mb(long bytes) => $"{bytes / (double)(1 << 20):F1} MB";
}
