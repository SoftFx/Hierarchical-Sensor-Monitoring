using System.Diagnostics;
using System.Reflection;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;

// Sealed weeks in their keep period (#1805 follow-up): the read cost of a sealed week whose overlay still holds every
// source row, the memory of the open databases, and a long run of server hours looking for a leak.
//
//   SealedHistoryBench --keep-period <work-dir> build [weeks=9] [sealed=7] [sensors=670]
//       <work-dir>/plain: the weeks as rows, compacted; <work-dir>/sealed: a copy with the oldest weeks sealed by the job
//       and still in their keep period (every source row is in the overlay).
//   SealedHistoryBench --keep-period <work-dir> reads
//       H1: time, managed allocations, GC counts and chunk decodes of each read, plain against sealed.
//   SealedHistoryBench --keep-period <work-dir> memory plain|sealed [rounds=3]
//       H2: process memory after the start, after the history load of every sensor and after rounds of page reads.
//   SealedHistoryBench --keep-period <work-dir> hours plain|sealed [hours=200] [every=20]
//       H3: hours of server life (reads, writes, hourly retention, sealing and compaction passes); the managed heap after
//       full collections, undisposed finalizable objects, leases, open files.
//
// Every phase but build works on a copy of the folder it names (the copy is replaced on every run).
internal static class KeepPeriodBench
{
    private static readonly HistorySealingSettings Sealing = new()
    {
        Enabled = true,
        SealAfter = TimeSpan.FromDays(1),
        KeepSourceRows = TimeSpan.FromDays(56), // the keep time of every sealed week lies weeks after today's real clock
    };


    public static void Run(string work, string phase, Dictionary<string, string> args)
    {
        work = Path.GetFullPath(work);

        // fresh=0: use the copy a "prepare" run made (in another container, so the copy's page cache is not charged to
        // the memory measured here).
        _fresh = Int(args, "fresh", 1) == 1;

        switch (phase)
        {
            case "prepare": FreshCopy(work, args.GetValueOrDefault("variant", "sealed")); break;
            case "build": Build(work, Int(args, "weeks", 9), Int(args, "sealed", 7), Int(args, "sensors", 670)); break;
            case "reads": Reads(work, Int(args, "runs", 15)); break;
            case "memory": Memory(work, args.GetValueOrDefault("variant", "sealed"), Int(args, "rounds", 3)); break;
            case "compression": Compression(work); break;
            case "hours": Hours(work, args.GetValueOrDefault("variant", "sealed"), Int(args, "hours", 200), Int(args, "every", 20)); break;
            default: throw new ArgumentException($"unknown phase {phase}");
        }
    }


    #region Build

    private static void Build(string work, int weeks, int sealedWeeks, int sensors)
    {
        var plain = Path.Combine(work, "plain");
        var sealedFolder = Path.Combine(work, "sealed");

        foreach (var folder in new[] { plain, sealedFolder })
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);

        // The weeks end two weeks before today, so the keep time of every sealed week lies after today's real clock.
        var thisMonday = DateTime.UtcNow.Date.AddDays(-(((int)DateTime.UtcNow.DayOfWeek + 6) % 7));
        var first = thisMonday.AddDays(-7 * (weeks + 1));
        var random = new Random(1805);
        var watch = Stopwatch.StartNew();
        long rows = 0;

        using (var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = plain }))
        {
            for (int w = 0; w < weeks; w++)
                rows += WriteWeek(core, first.AddDays(7 * w), sensors, random);

            core.Compact();
            Report.Line($"plain: {weeks} weeks, {rows:N0} rows in {watch.Elapsed.TotalSeconds:F0} s; history {Mb(core.SensorHistoryDbSize)}");
        }

        CopyWeeks(plain, sealedFolder);

        using (var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = sealedFolder }))
        {
            // The oldest `sealedWeeks` weeks are due; the newer ones are not.
            var now = first.AddDays(7 * sealedWeeks) + Sealing.SealAfter;

            for (int i = 0; i < sealedWeeks + 2; i++)
            {
                var report = core.RunHistorySealingPass(now, Sealing);
                Report.Line($"  {report.Action} {report.Outcome} {Path.GetFileName(report.Week)}: {report.RowsSealed:N0} rows, {report.ChunkBytes:N0} chunk bytes");

                if (report.Outcome != HistorySealingOutcome.Done)
                    break;
            }

            Report.Line($"sealed: {core.SensorValuesDatabases.Count(db => ((SensorValuesWeek)db).IsSealed)} of {weeks} weeks sealed, keep period until {now + Sealing.KeepSourceRows:O}; history {Mb(core.SensorHistoryDbSize)}");
        }

        foreach (var dir in Directory.GetDirectories(sealedFolder).Order())
            Report.Line($"  {Path.GetFileName(dir)}: {Mb(Size(dir))}");
    }

    // 670 sensors of every type, one value every 2 to 40 minutes, a few File sensors, and one dense Double sensor (one
    // value per second for a day): the same ids every week. About 690,000 rows and 75 MB per week.
    private static long WriteWeek(DatabaseCore core, DateTime monday, int sensors, Random random)
    {
        SensorType[] types = [SensorType.Boolean, SensorType.Integer, SensorType.Double, SensorType.String, SensorType.IntegerBar, SensorType.DoubleBar, SensorType.TimeSpan, SensorType.Version, SensorType.Rate, SensorType.Enum];

        var from = monday.Ticks;
        var to = monday.AddDays(7).Ticks;
        long rows = 0;

        // A comment repeated on every value, when given. Note: the Linux build of the native LevelDB has no Snappy (its
        // tables are stored uncompressed, about 110 B per row here against 35 on Windows), so on Linux row blocks are
        // never in LevelDB's block cache: they are read through the memory-mapped table files.
        void Sensor(Guid id, SensorType type, long step, long until, string comment)
        {
            double level = 50;

            for (long tick = from + random.NextInt64(0, step); tick < until; tick += step + random.NextInt64(-step / 50, step / 50 + 1))
            {
                level = Math.Round(level + random.Next(-100, 101) / 100.0, 2);
                var value = Value(type, tick, level, random);
                core.AddSensorValue(id, comment is null ? value : value with { Comment = comment });
                rows++;
            }
        }

        var ids = SensorIds(sensors);

        Sensor(ids[0], SensorType.Double, TimeSpan.TicksPerSecond, from + TimeSpan.TicksPerDay, null);

        for (int i = 1; i < ids.Count; i++)
            Sensor(ids[i], i % 100 == 0 ? SensorType.File : types[i % types.Length], 2 * TimeSpan.TicksPerMinute * (1 + i % 20), to,
                null);

        return rows;
    }

    private static List<Guid> SensorIds(int count)
    {
        var ids = new Random(1804);
        var list = new List<Guid>(count);

        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[16];
            ids.NextBytes(bytes);
            list.Add(new Guid(bytes));
        }

        return list;
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
            SensorType.String => new StringValue { Value = $"state {random.Next(5)} at {level:F2}" },
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

    #endregion

    // Whether this platform's native LevelDB compresses blocks: 20,000 values of 1 KB of zeros, compacted.
    private static void Compression(string work)
    {
        var path = Path.Combine(work, "compression-probe");

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            for (int i = 0; i < 20_000; i++)
                db.Put(BitConverter.GetBytes(i), new byte[1024]);

            db.Compact();
        }

        var tables = new DirectoryInfo(path).GetFiles("*.ldb").Sum(f => f.Length);
        Report.Line($"20,000 values of 1 KB of zeros (20.0 MB) after compaction: tables {Mb(tables)} -> {(tables < 5 << 20 ? "blocks are compressed" : "blocks are NOT compressed")}");
        Directory.Delete(path, recursive: true);
    }

    #region H1: reads

    private static void Reads(string work, int runs)
    {
        var results = new Dictionary<string, List<string>>();

        foreach (var variant in new[] { "plain", "sealed" })
        {
            var folder = FreshCopy(work, variant);
            using var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = folder });

            var weeks = core.SensorValuesDatabases; // oldest first
            var sensors = SensorIdsOf(weeks[^1]);
            var week = weeks[1];                    // a sealed week in the sealed copy
            var lines = new List<string>();

            Report.Line($"== {variant}: {weeks.Count} weeks, {weeks.Count(w => ((SensorValuesWeek)w).IsSealed)} sealed; {sensors.Count} sensors; week under test {Path.GetFileName(week.Name)} (sealed: {((SensorValuesWeek)week).IsSealed})");

            // A 2-minute sensor (about 5,000 rows in the week) and the dense sensor (86,400 rows).
            foreach (var (label, sensor) in new[] { ("2-min sensor", sensors[20]), ("dense sensor", sensors[0]) })
            {
                var min = new DbKey(sensor, 0).ToBytes();
                var max = new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes();
                var prefix = new DbKey(sensor, 0).ToPrefixBytes();

                lines.Add(Measure(core, $"{label}: GetLatest", runs, () => week.GetLatest(max, prefix)));
                lines.Add(Measure(core, $"{label}: GetFirst", runs, () => week.GetFirst(min, prefix)));
                lines.Add(Measure(core, $"{label}: page of 100, newest first", runs, () => week.GetValuesTo(min, max).Take(100).Count()));
                lines.Add(Measure(core, $"{label}: page of 100, oldest first", runs, () => week.GetValuesFrom(min, max).Take(100).Count()));
                lines.Add(Measure(core, $"{label}: whole sensor in the week", Math.Max(3, runs / 3), () => week.GetValuesFrom(min, max).Count()));
            }

            // The history load of every sensor (BaseSensorModelT.LoadHistoryUnderLock): GetLatestValue + GetFirstValue.
            lines.Add(Measure(core, $"history load, {sensors.Count} sensors, {weeks.Count} weeks", 5, () =>
            {
                foreach (var sensor in sensors)
                {
                    core.GetLatestValue(sensor, DateTime.MaxValue.Ticks);
                    core.GetFirstValue(sensor);
                }

                return 0;
            }));

            // The same load as of the end of the oldest sealed week: GetLatestValue walks every newer week first.
            var asOf = weeks[1].To - 1;
            lines.Add(Measure(core, $"GetLatestValue as of week 1, {sensors.Count} sensors", 5, () =>
            {
                foreach (var sensor in sensors)
                    core.GetLatestValue(sensor, asOf);

                return 0;
            }));

            // A page of 100 of each sensor in each week, both directions (the history table of the web UI).
            lines.Add(Measure(core, $"page of 100 both ways, every sensor and week", 3, () =>
            {
                foreach (var w in weeks)
                    foreach (var sensor in sensors)
                    {
                        var min = new DbKey(sensor, 0).ToBytes();
                        var max = new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes();
                        w.GetValuesTo(min, max).Take(100).Count();
                        w.GetValuesFrom(min, max).Take(100).Count();
                    }

                return 0;
            }));

            results[variant] = lines;
        }

        Report.Line();
        Report.Line($"{"read",-58} {"plain",-52} | sealed");

        for (int i = 0; i < results["plain"].Count; i++)
        {
            var p = results["plain"][i].Split('|');
            var s = results["sealed"][i].Split('|');
            Report.Line($"{p[0],-58} {p[1],-52} | {s[1]}");
        }
    }

    // Median time, allocated bytes and GC collections per call, and chunk decodes per call; "name|figures".
    private static string Measure(DatabaseCore core, string name, int runs, Func<object> read)
    {
        read(); // warm-up: opens the weeks, fills the caches

        var times = new List<double>();
        var decodes = Decodes(core);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < runs; i++)
        {
            var watch = Stopwatch.StartNew();
            read();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / runs;
        decodes = (Decodes(core) - decodes) / runs;

        return $"{name}|{Report.Median(times),9:F3} ms {allocated / 1024.0,10:F1} KB {decodes,6} dec  GC {GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}";
    }

    private static long Decodes(DatabaseCore core) =>
        core.SensorValuesDatabases.Select(db => ((SensorValuesWeek)db).Current).OfType<SealedSensorValuesDatabase>().Sum(s => s.ChunkReader.Decodes);

    // The sensors of the newest week, in the order the build generated them (index 0 is the dense sensor, index i has a
    // value every 2 * (1 + i % 20) minutes).
    private static List<Guid> SensorIdsOf(ISensorValuesDatabase week)
    {
        var stored = week.GetAll().Select(kv => DbKey.FromBytes(kv.key).SensorId).ToHashSet();
        var generated = SensorIds(stored.Count);

        if (!stored.SetEquals(generated))
            throw new InvalidOperationException("the newest week does not hold the generated sensors");

        return generated;
    }

    #endregion

    #region H2: memory

    private static void Memory(string work, string variant, int rounds)
    {
        var folder = FreshCopy(work, variant);

        Report.Line($"== memory, {variant}, GC {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}, {Environment.ProcessorCount} CPUs, " +
                    $"GC heap hard limit {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1 << 20)} MB");
        Snapshot("before the database", null);

        using var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = folder });

        Snapshot("database created (no week opened)", core);

        var weeks = core.SensorValuesDatabases; // opens every week, as the history load does
        Snapshot("every week opened, nothing read", core);

        var sensors = SensorIdsOf(weeks[^1]);
        Snapshot("after reading the sensor ids from the newest week", core);

        foreach (var sensor in sensors)
        {
            core.GetLatestValue(sensor, DateTime.MaxValue.Ticks);
            core.GetFirstValue(sensor);
        }

        Snapshot("after the history load of every sensor", core);

        for (int round = 1; round <= rounds; round++)
        {
            foreach (var w in weeks)
                foreach (var sensor in sensors)
                {
                    var min = new DbKey(sensor, 0).ToBytes();
                    var max = new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes();
                    w.GetValuesTo(min, max).Take(100).Count();
                    w.GetValuesFrom(min, max).Take(100).Count();
                }

            Snapshot($"after page reads, round {round} (every sensor, every week, both ways)", core);
        }

        foreach (var w in weeks)
            w.GetAll().Count();

        Snapshot("after a full read of every week (GetAll)", core);

        LevelDbProperties(core);
    }

    #endregion

    #region H3: hours

    private static void Hours(string work, string variant, int hours, int every)
    {
        var folder = FreshCopy(work, variant);
        using var core = new DatabaseCore(new DatabaseSettings { DatabaseFolder = folder });

        var weeks = core.SensorValuesDatabases;
        var sensors = SensorIdsOf(weeks[^1]);
        var random = new Random(1805);
        var oldest = weeks[0];

        // The clock: one hour after the newest week ends. Retention keeps the history from the oldest week's start
        // onwards and moves its cutoff by an hour every hour, through the oldest (sealed) week.
        var now = new DateTime(weeks[^1].To, DateTimeKind.Utc).AddHours(1);
        var cutoffs = sensors.ToDictionary(s => s, _ => DateTime.MinValue);
        var cutoff = new DateTime(oldest.From, DateTimeKind.Utc);
        var settings = Sealing with { SealAfter = TimeSpan.FromDays(3650) }; // nothing becomes due

        Report.Line($"== hours, {variant}: {weeks.Count} weeks ({weeks.Count(w => ((SensorValuesWeek)w).IsSealed)} sealed), {sensors.Count} sensors, {hours} hours");
        Snapshot("start", core);

        // The history load at the start.
        foreach (var sensor in sensors)
        {
            core.GetLatestValue(sensor, DateTime.MaxValue.Ticks);
            core.GetFirstValue(sensor);
        }

        Snapshot("after the history load", core);

        var abandoned = new List<IEnumerator<byte[]>>();

        for (int hour = 1; hour <= hours; hour++)
        {
            now = now.AddHours(1);

            // Writes: one value per sensor per minute into the current week, and a few late values into sealed weeks.
            for (int minute = 0; minute < 60; minute++)
                foreach (var sensor in sensors)
                    core.AddSensorValue(sensor, Value(SensorType.Double, now.AddMinutes(minute - 60).Ticks, minute, random));

            for (int i = 0; i < 20; i++)
            {
                var w = weeks[random.Next(1, weeks.Count)];
                core.AddSensorValue(sensors[random.Next(1, sensors.Count)], Value(SensorType.Double, w.From + random.NextInt64(w.To - w.From), i, random));
            }

            // Reads: the history load of 50 sensors, pages of 100 both ways for 50 sensors in random weeks, and the
            // first page of a range read abandoned without disposal every tenth hour (a client that went away).
            for (int i = 0; i < 50; i++)
            {
                var sensor = sensors[random.Next(sensors.Count)];
                core.GetLatestValue(sensor, DateTime.MaxValue.Ticks);
                core.GetFirstValue(sensor);

                var w = weeks[random.Next(weeks.Count)];
                var min = new DbKey(sensor, 0).ToBytes();
                var max = new DbKey(sensor, DateTime.MaxValue.Ticks).ToBytes();
                w.GetValuesTo(min, max).Take(100).Count();
                w.GetValuesFrom(min, max).Take(100).Count();
            }

            if (hour % 10 == 0)
            {
                var enumerator = weeks[1].GetValuesTo(new DbKey(sensors[5], 0).ToBytes(), new DbKey(sensors[5], DateTime.MaxValue.Ticks).ToBytes()).GetEnumerator();
                enumerator.MoveNext();
                abandoned.Add(enumerator);

                if (abandoned.Count > 3)
                    abandoned.RemoveAt(0); // dropped without Dispose: the garbage collector finalizes its iterators
            }

            // Hourly retention through the oldest week: a prefix delete of every sensor.
            var previous = cutoff;
            cutoff = cutoff.AddHours(1);

            if (cutoff < new DateTime(oldest.To, DateTimeKind.Utc))
                foreach (var sensor in sensors)
                {
                    core.ClearSensorValues(sensor, cutoffs[sensor] == DateTime.MinValue ? DateTime.MinValue : previous, cutoff);
                    cutoffs[sensor] = cutoff;
                }

            // A sealing pass every 7 minutes (nothing due: every sealed week keeps its rows) and a compaction step every 10.
            for (int i = 0; i < 8; i++)
                core.RunHistorySealingPass(now, settings);

            for (int i = 0; i < 6; i++)
                core.CompactNextDueHistoryShard(now, TimeSpan.FromHours(3));

            if (hour % every == 0 || hour == hours)
                Snapshot($"hour {hour}", core);
        }
    }

    #endregion

    #region Measurement

    private static void Snapshot(string what, DatabaseCore core)
    {
        // Undisposed finalizable objects (LevelDB iterators, snapshots, options are finalizable) found dead by a full GC.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var pending = GC.GetGCMemoryInfo(GCKind.FullBlocking).FinalizationPendingCount;
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        var heap = GC.GetTotalMemory(false);
        var status = ProcStatus();
        var cgroup = Cgroup();

        var leases = core is null ? "" : $" leases {Leases(core)}";

        Report.Line($"{what,-70} heap {Mb(heap),9} committed {Mb(info.TotalCommittedBytes),9} " +
                    $"RSS {status.GetValueOrDefault("VmRSS", "?"),11} anon {status.GetValueOrDefault("RssAnon", "?"),11} file {status.GetValueOrDefault("RssFile", "?"),11} " +
                    $"fds {Fds(),4} ldb maps {LdbMaps(),4}{cgroup} finalizable {pending}{leases}");
    }

    private static Dictionary<string, string> ProcStatus()
    {
        var result = new Dictionary<string, string>();

        if (!File.Exists("/proc/self/status"))
        {
            using var process = Process.GetCurrentProcess();
            result["VmRSS"] = $"{process.WorkingSet64 / 1024} kB";
            result["RssAnon"] = $"{process.PrivateMemorySize64 / 1024} kB(priv)";
            return result;
        }

        foreach (var line in File.ReadAllLines("/proc/self/status"))
        {
            var parts = line.Split(':', 2);

            if (parts.Length == 2 && parts[0] is "VmRSS" or "RssAnon" or "RssFile")
                result[parts[0]] = parts[1].Trim();
        }

        return result;
    }

    // The container's memory as the cgroup counts it; docker stats shows the usage less the inactive file pages.
    private static string Cgroup()
    {
        static Dictionary<string, long> Stat(string path) =>
            File.ReadAllLines(path).Select(l => l.Split(' ')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => long.Parse(p[1]));

        if (File.Exists("/sys/fs/cgroup/memory.current")) // v2
        {
            var current = long.Parse(File.ReadAllText("/sys/fs/cgroup/memory.current").Trim());
            var stat = Stat("/sys/fs/cgroup/memory.stat");

            return $" cgroup {Mb(current)} (stats {Mb(current - stat.GetValueOrDefault("inactive_file"))}, anon {Mb(stat.GetValueOrDefault("anon"))}, file {Mb(stat.GetValueOrDefault("file"))})";
        }

        if (File.Exists("/sys/fs/cgroup/memory/memory.usage_in_bytes")) // v1
        {
            var usage = long.Parse(File.ReadAllText("/sys/fs/cgroup/memory/memory.usage_in_bytes").Trim());
            var stat = Stat("/sys/fs/cgroup/memory/memory.stat");

            return $" cgroup {Mb(usage)} (stats {Mb(usage - stat.GetValueOrDefault("total_inactive_file"))}, rss {Mb(stat.GetValueOrDefault("total_rss"))}, " +
                   $"mapped {Mb(stat.GetValueOrDefault("total_mapped_file"))})";
        }

        return "";
    }

    private static int Fds() => Directory.Exists("/proc/self/fd") ? Directory.GetFiles("/proc/self/fd").Length : -1;

    private static int LdbMaps() => File.Exists("/proc/self/maps")
        ? File.ReadLines("/proc/self/maps").Where(l => l.EndsWith(".ldb", StringComparison.Ordinal)).Select(l => l[(l.LastIndexOf(' ') + 1)..]).Distinct().Count()
        : -1;

    // References held on the instance serving each week: 1 is the handle's own; each running or abandoned read adds one.
    private static string Leases(DatabaseCore core)
    {
        var field = typeof(SensorValuesWeek).GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance);
        var parts = new List<string>();

        foreach (var shard in core.SensorValuesDatabases.Cast<SensorValuesWeek>())
        {
            var lease = field.GetValue(shard);
            var references = (int)lease.GetType().GetField("_references", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lease);
            parts.Add($"{references}{(shard.ReplacedInUse > 0 ? $"+{shard.ReplacedInUse}r" : "")}");
        }

        return string.Join(",", parts);
    }

    private static void LevelDbProperties(DatabaseCore core)
    {
        foreach (var week in core.SensorValuesDatabases.Cast<SensorValuesWeek>())
        {
            var rows = ((IntervalDataseBase)week.Current).RowDatabase.Native;
            var line = $"  {Path.GetFileName(week.Name)}: rows {Property(rows)}";

            if (week.Current is SealedSensorValuesDatabase store)
                line += $"; chunks {Property(store.ChunkDatabase.Native)}";

            Report.Line(line);
        }
    }

    private static string Property(LevelDB.DB db)
    {
        var memory = db.PropertyValue("leveldb.approximate-memory-usage");
        var tables = db.PropertyValue("leveldb.sstables")?.Split('\n').Count(l => l.Contains(':') && !l.StartsWith("---")) ?? -1;

        // Block cache plus memtables, as LevelDB counts them.
        return $"approximate memory {(long.TryParse(memory, out var m) ? $"{m / 1024} KB" : memory ?? "n/a")}, {tables} tables";
    }

    #endregion

    private static bool _fresh = true;

    private static string FreshCopy(string work, string variant)
    {
        var source = Path.Combine(work, variant);
        var copy = Path.Combine(work, $"run-{variant}");

        if (!_fresh && Directory.Exists(copy))
            return copy;

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"{source}: run the build phase first");

        if (Directory.Exists(copy))
            Directory.Delete(copy, recursive: true);

        CopyWeeks(source, copy);
        return copy;
    }

    // The weekly databases only (row and chunk directories): the other databases are created empty on open.
    private static void CopyWeeks(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var directory in Directory.GetDirectories(from))
            if (Path.GetFileName(directory).StartsWith("SensorValues", StringComparison.Ordinal))
                CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from))
            if (Path.GetFileName(file) != "LOCK")
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    private static int Int(Dictionary<string, string> args, string key, int fallback) => args.TryGetValue(key, out var value) ? int.Parse(value) : fallback;

    private static long Size(string path) => new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private static string Mb(long bytes) => $"{bytes / (double)(1 << 20):F1} MB";
}
