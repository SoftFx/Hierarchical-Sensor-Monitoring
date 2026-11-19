using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1803: nothing above DatabaseCore notices that a week is sealed. Two databases get the same values; one of
// them has a closed week sealed; every DatabaseCore call that reads or changes history answers the same.
public sealed class SealedWeekDatabaseCoreTests : IDisposable
{
    private static readonly DateTime SealedWeekDay = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc); // a Wednesday
    private static readonly DateTime OtherWeekDay = SealedWeekDay.AddDays(-7);

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-1803-{Guid.NewGuid():N}");
    private readonly DatabaseSettings _plainSettings;
    private readonly DatabaseSettings _sealedSettings;
    private readonly Random _random = new(1803);

    private DatabaseCore _plain;
    private DatabaseCore _sealed;
    private FidelityComparer _compare;
    private List<SealedChunk> _sealedChunks = [];

    // Fixed ids: where a sensor sorts among the others decides which chunks a seek passes (#1803 review R4).
    private readonly Guid _dense = new("6a1e0c1d-0000-4000-8000-000000000001");
    private readonly Guid _bar = new("2b7f4e90-0000-4000-8000-000000000002");
    private readonly Guid _file = new("c3d2a511-0000-4000-8000-000000000003");
    private readonly Guid _sparse = new("8e4b7c02-0000-4000-8000-000000000004");
    private readonly Guid _target = new("f0a9d6e3-0000-4000-8000-000000000005");
    private readonly Guid _unknown = new("47c18b24-0000-4000-8000-000000000006");


    public SealedWeekDatabaseCoreTests(ITestOutputHelper output)
    {
        _output = output;
        _plainSettings = new DatabaseSettings { DatabaseFolder = Path.Combine(_root, "plain"), ExportFolder = Path.Combine(_root, "plain-export") };
        _sealedSettings = new DatabaseSettings { DatabaseFolder = Path.Combine(_root, "sealed"), ExportFolder = Path.Combine(_root, "sealed-export") };
    }

    public void Dispose()
    {
        _plain?.Dispose();
        _sealed?.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    [Fact]
    public void EveryHistoryCallOfDatabaseCore_AnswersTheSameForASealedWeek()
    {
        _plain = new DatabaseCore(_plainSettings);
        _sealed = new DatabaseCore(_sealedSettings);

        Fill(_dense, SensorType.Double, SealedWeekDay, 9_000, TimeSpan.FromSeconds(20));
        Fill(_bar, SensorType.DoubleBar, SealedWeekDay, 700, TimeSpan.FromMinutes(1));
        Fill(_file, SensorType.File, SealedWeekDay, 30, TimeSpan.FromHours(1));
        Fill(_sparse, SensorType.Integer, SealedWeekDay, 5, TimeSpan.FromHours(5));
        Fill(_dense, SensorType.Double, OtherWeekDay, 200, TimeSpan.FromMinutes(1));
        Fill(_target, SensorType.Double, SealedWeekDay.AddSeconds(20 * 100), 50, TimeSpan.FromSeconds(20)); // shares ticks with _dense

        SealTheWeek();

        var week = _sealed.SensorValuesDatabases.Single(db => db.Contains(SealedWeekDay.Ticks));
        Assert.IsType<SealedSensorValuesDatabase>(week);
        Assert.Equal(_plain.SensorValuesDatabases.Select(db => Path.GetFileName(db.Name)), _sealed.SensorValuesDatabases.Select(db => Path.GetFileName(db.Name)));

        AssertSameHistory("after sealing");
        AssertSizes();
        AssertSameExport(week.Name);

        // Late values, rewrites, a sensor move, deletes.
        var rewrite = Value(SensorType.Double, SealedWeekDay.AddSeconds(20 * 3000));
        _plain.AddSensorValue(_dense, rewrite);
        _sealed.AddSensorValue(_dense, rewrite);

        var late = Value(SensorType.Double, SealedWeekDay.AddSeconds(20 * 3000 + 7));
        _plain.AddSensorValue(_dense, late);
        _sealed.AddSensorValue(_dense, late);

        Assert.Equal(_plain.MergeSensorValues(_dense, _target), _sealed.MergeSensorValues(_dense, _target));
        AssertSameHistory("after the merge");

        foreach (var core in new[] { _plain, _sealed })
        {
            core.ClearSensorValues(_dense, SealedWeekDay.AddSeconds(20 * 4000), SealedWeekDay.AddSeconds(20 * 4500)); // across a chunk border
            core.ClearSensorValues(_target, DateTime.MinValue, SealedWeekDay.AddSeconds(20 * 2000));
            core.RemoveSensorWithMetadata(_bar);
        }

        AssertSameHistory("after the deletes");

        // And after a restart.
        _sealed.Dispose();
        _sealed = Open(_sealedSettings);
        AssertSameHistory("after a restart");

        _output.WriteLine($"fidelity matches: {_compare.FidelityMatches}");
        Assert.True(_compare.FidelityMatches > 0);
    }

    [Fact]
    public void IncompleteSealedDirectory_IsNotAWeek_AndACompleteOneIsEvenWithoutItsRows()
    {
        var day = SealedWeekDay;
        var from = DateTimeMethods.GetStartOfWeekTicks(day.Ticks);
        var to = DateTimeMethods.GetEndOfWeekTicks(day.Ticks);
        var settings = (IDatabaseSettings)_sealedSettings;

        _sealed = new DatabaseCore(_sealedSettings);
        _sealed.AddSensorValue(_dense, Value(SensorType.Double, day));
        _sealed.Dispose();

        // The rows of the week go into a sealed directory; the row directory is then removed.
        WeekSealer.Seal(settings.GetPathToSensorValueDatabase(from, to), settings.GetPathToSealedSensorValueDatabase(from, to));
        Directory.Delete(settings.GetPathToSensorValueDatabase(from, to), recursive: true);

        _sealed = Open(_sealedSettings);
        Assert.Single(_sealed.SensorValuesDatabases);
        Assert.NotNull(_sealed.GetLatestValue(_dense, DateTime.MaxValue.Ticks));
        _sealed.Dispose();

        // Without its marker the same directory is no week at all.
        File.Delete(Path.Combine(settings.GetPathToSealedSensorValueDatabase(from, to), SealedWeekMarker.FileName));
        Directory.Delete(settings.GetPathToSensorValueDatabase(from, to), recursive: true);

        _sealed = Open(_sealedSettings);
        Assert.Empty(_sealed.SensorValuesDatabases);
    }


    // A corrupt chunk must not turn into "this week has no value": DatabaseCore would answer with another week's.
    [Fact]
    public void CorruptLastChunk_FailsLatestValue_InsteadOfAnsweringFromAnOlderWeek()
    {
        _plain = Open(_plainSettings);
        _sealed = Open(_sealedSettings);
        Fill(_dense, SensorType.Double, SealedWeekDay, 9_000, TimeSpan.FromSeconds(20));
        Fill(_file, SensorType.File, SealedWeekDay, 3, TimeSpan.FromHours(1));
        Fill(_dense, SensorType.Double, OtherWeekDay, 20, TimeSpan.FromMinutes(1));          // an older week
        Fill(_sparse, SensorType.Integer, SealedWeekDay.AddDays(7), 20, TimeSpan.FromMinutes(1)); // a newer week, another sensor
        Fill(_sparse, SensorType.Integer, SealedWeekDay, 20, TimeSpan.FromMinutes(1));

        SealTheWeek(beforeOpen: (chunkPath, seal) =>
        {
            var chunks = seal.Chunks.Where(c => c.SensorId == _dense).OrderBy(c => c.FirstTick).ToList();
            CorruptChunk(chunkPath, chunks[0]);
            CorruptChunk(chunkPath, chunks[^1]);
        });

        var before = SealedHistoryDiagnostics.ChunkReadFailures;

        // The newest value is in the corrupt last chunk; the older week holds values too.
        Assert.ThrowsAny<SealedChunkReadException>(() => _sealed.GetLatestValue(_dense, DateTime.MaxValue.Ticks));
        // A point inside the corrupt first chunk: the answer would otherwise come from the older week.
        Assert.ThrowsAny<SealedChunkReadException>(() => _sealed.GetLatestValue(_dense, SealedWeekDay.AddMinutes(30).Ticks));
        Assert.True(SealedHistoryDiagnostics.ChunkReadFailures > before);

        // The history size of the sensor does not throw: the two unreadable chunks count no rows (a lower bound).
        var sealedChunks = _sealedChunks.Where(c => c.SensorId == _dense).OrderBy(c => c.FirstTick).ToList();
        var plainSize = _plain.CalculateSensorHistorySize(_dense);
        var sealedSize = _sealed.CalculateSensorHistorySize(_dense);
        Assert.Equal(plainSize.dateCnt - sealedChunks[0].Rows - sealedChunks[^1].Rows, sealedSize.dateCnt);
        Assert.True(sealedSize.valueSize > 0);

        // A sensor without a corrupt chunk reads as before.
        _compare.Value(_plain.GetLatestValue(_sparse, DateTime.MaxValue.Ticks), _sealed.GetLatestValue(_sparse, DateTime.MaxValue.Ticks), "latest of another sensor");
        _compare.Value(_plain.GetFirstValue(_sparse), _sealed.GetFirstValue(_sparse), "first of another sensor");
    }

    [Fact]
    public void CorruptFirstChunk_FailsFirstValue_InsteadOfAnsweringFromALaterWeek()
    {
        _plain = Open(_plainSettings);
        _sealed = Open(_sealedSettings);
        Fill(_dense, SensorType.Double, SealedWeekDay, 9_000, TimeSpan.FromSeconds(20));
        Fill(_file, SensorType.File, SealedWeekDay, 3, TimeSpan.FromHours(1));
        Fill(_dense, SensorType.Double, SealedWeekDay.AddDays(7), 20, TimeSpan.FromMinutes(1)); // a later week

        SealTheWeek(beforeOpen: (chunkPath, seal) =>
            CorruptChunk(chunkPath, seal.Chunks.Where(c => c.SensorId == _dense).OrderBy(c => c.FirstTick).First()));

        Assert.ThrowsAny<SealedChunkReadException>(() => _sealed.GetFirstValue(_dense));
    }

    private static void CorruptChunk(string chunkPath, SealedChunk chunk)
    {
        using var chunks = new LevelDBDatabaseAdapter(chunkPath);
        var key = new DbKey(chunk.SensorId, chunk.FirstTick).ToBytes();

        Assert.True(chunks.TryRead(key, out var bytes));
        bytes[bytes.Length / 2] ^= 0x5A;
        chunks.Put(key, bytes);
    }

    [Fact]
    public void Compaction_OfASealedWeek_ReportsTheSizeOfBothDirectories()
    {
        _plain = Open(_plainSettings);
        _sealed = Open(_sealedSettings);
        Fill(_dense, SensorType.Double, SealedWeekDay, 9_000, TimeSpan.FromSeconds(20));
        Fill(_file, SensorType.File, SealedWeekDay, 3, TimeSpan.FromHours(1));
        SealTheWeek();

        var settings = (IDatabaseSettings)_sealedSettings;
        var from = DateTimeMethods.GetStartOfWeekTicks(SealedWeekDay.Ticks);
        var to = DateTimeMethods.GetEndOfWeekTicks(SealedWeekDay.Ticks);
        var rowPath = settings.GetPathToSensorValueDatabase(from, to);
        var chunkSize = DirectorySize(settings.GetPathToSealedSensorValueDatabase(from, to));

        (long before, long after) Reported()
        {
            var text = File.ReadAllText(Path.Combine(rowPath, CompactedShardMarker.FileName));
            var sizes = text[(text.IndexOf(": ", StringComparison.Ordinal) + 2)..].Split(' ');
            return (long.Parse(sizes[0]), long.Parse(sizes[2]));
        }

        // The background step.
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, _sealed.CompactNextDueHistoryShard(DateTime.UtcNow, TimeSpan.Zero));
        var (before, after) = Reported();
        _output.WriteLine($"background: {before} -> {after}; chunk directory {chunkSize}");
        Assert.True(before >= chunkSize && after >= chunkSize / 2, $"{before} -> {after} does not include the {chunkSize} bytes of chunks");

        // The manual path.
        File.Delete(Path.Combine(rowPath, CompactedShardMarker.FileName));
        _sealed.Compact();
        (before, after) = Reported();
        Assert.True(before >= chunkSize / 2 && after >= chunkSize / 2, $"{before} -> {after} does not include the chunks");
    }

    private void Fill(Guid sensor, SensorType type, DateTime start, int count, TimeSpan step)
    {
        for (int i = 0; i < count; i++)
        {
            var value = Value(type, start + step * i);
            _plain.AddSensorValue(sensor, value);
            _sealed.AddSensorValue(sensor, value);
        }
    }

    private BaseValue Value(SensorType type, DateTime time) => TestValues.Make(type, time.Ticks, _random);

    private void SealTheWeek(Action<string, SealResult> beforeOpen = null)
    {
        _sealed.Dispose();

        var settings = (IDatabaseSettings)_sealedSettings;
        var from = DateTimeMethods.GetStartOfWeekTicks(SealedWeekDay.Ticks);
        var to = DateTimeMethods.GetEndOfWeekTicks(SealedWeekDay.Ticks);
        var seal = WeekSealer.Seal(settings.GetPathToSensorValueDatabase(from, to), settings.GetPathToSealedSensorValueDatabase(from, to));

        _output.WriteLine($"sealed {seal.SealedRows} rows into {seal.Chunks.Count} chunks; {seal.RefusedSensors} sensor(s) refused");
        Assert.Equal(1, seal.RefusedSensors); // File

        _compare = new FidelityComparer(seal.SealedOriginals);
        _sealedChunks = seal.Chunks;
        beforeOpen?.Invoke(settings.GetPathToSealedSensorValueDatabase(from, to), seal);
        _sealed = Open(_sealedSettings);
    }

    // DatabaseCore.Dispose leaves the layout, change-set and snapshot databases open (their LOCK is held until
    // the process ends), so a restarted core gets fresh ones; the history shards it reopens are the same.
    private static DatabaseCore Open(DatabaseSettings settings)
    {
        var restart = Guid.NewGuid().ToString("N");

        return new DatabaseCore(settings with
        {
            ServerLayoutDatabaseName = $"ServerLayout{restart}",
            ChangeSetsDatabaseName = $"ChangeSets{restart}",
            SnaphotsDatabaseName = $"Snapshots{restart}",
        });
    }

    private void AssertSameHistory(string when)
    {
        var sensors = new[] { _dense, _bar, _file, _sparse, _target, _unknown };
        var weekStart = new DateTime(DateTimeMethods.GetStartOfWeekTicks(SealedWeekDay.Ticks), DateTimeKind.Utc);
        var ranges = new (DateTime from, DateTime to)[]
        {
            (DateTime.MinValue, DateTime.MaxValue),
            (weekStart, weekStart.AddDays(7)),
            (SealedWeekDay.AddHours(10), SealedWeekDay.AddHours(30)),
            (OtherWeekDay, SealedWeekDay.AddHours(1)),
        };

        foreach (var sensor in sensors)
        {
            foreach (var (from, to) in ranges)
            {
                foreach (var count in new[] { 50, -50, 333, -333, 100_000, -100_000 })
                {
                    var plainPages = _plain.GetSensorValuesPage(sensor, from, to, count).ToBlockingEnumerable().Select(p => p.ToList()).ToList();
                    var sealedPages = _sealed.GetSensorValuesPage(sensor, from, to, count).ToBlockingEnumerable().Select(p => p.ToList()).ToList();

                    Assert.Equal(plainPages.Count, sealedPages.Count);
                    for (int i = 0; i < plainPages.Count; i++)
                        _compare.Values(plainPages[i], sealedPages[i], $"{when}: page {i} of {sensor} {from:O}..{to:O} x{count}");
                }

                _compare.Values(_plain.GetSensorValues(sensor, from, to).ToBlockingEnumerable(), _sealed.GetSensorValues(sensor, from, to).ToBlockingEnumerable(), $"{when}: values of {sensor}");
            }

            foreach (var to in new[] { DateTime.MaxValue.Ticks, SealedWeekDay.AddHours(12).Ticks, OtherWeekDay.AddHours(12).Ticks })
                _compare.Value(_plain.GetLatestValue(sensor, to), _sealed.GetLatestValue(sensor, to), $"{when}: latest of {sensor} at {to}");

            _compare.Value(_plain.GetFirstValue(sensor), _sealed.GetFirstValue(sensor), $"{when}: first of {sensor}");

            Assert.Equal(_plain.CalculateSensorHistorySize(sensor).dateCnt, _sealed.CalculateSensorHistorySize(sensor).dateCnt);
        }

        var plainFirstLast = _plain.GetLastAndFirstValues(sensors);
        var sealedFirstLast = _sealed.GetLastAndFirstValues(sensors);
        Assert.Equal(plainFirstLast.Keys.OrderBy(k => k), sealedFirstLast.Keys.OrderBy(k => k));
        foreach (var (id, (first, last)) in plainFirstLast)
        {
            _compare.Value(first, sealedFirstLast[id].Item1, $"{when}: first of {id}");
            _compare.Value(last, sealedFirstLast[id].Item2, $"{when}: last of {id}");
        }

        var request = sensors.ToDictionary(id => id, id => (SealedWeekDay.Ticks, SealedWeekDay.AddHours(20).Ticks));
        var plainLatest = _plain.GetLatestValuesFromTo(request);
        var sealedLatest = _sealed.GetLatestValuesFromTo(request);
        foreach (var (id, value) in plainLatest)
            _compare.Value(value, sealedLatest[id], $"{when}: latest from-to of {id}");
    }

    // The sealed week reports what it stores: the chunks, much smaller than the rows they replace.
    private void AssertSizes()
    {
        var plainSize = _plain.CalculateSensorHistorySize(_dense);
        var sealedSize = _sealed.CalculateSensorHistorySize(_dense);

        _output.WriteLine($"dense sensor: plain {plainSize.keySize + plainSize.valueSize} bytes, sealed {sealedSize.keySize + sealedSize.valueSize} bytes, {plainSize.dateCnt} values");
        Assert.Equal(plainSize.dateCnt, sealedSize.dateCnt);
        Assert.True(sealedSize.keySize + sealedSize.valueSize < (plainSize.keySize + plainSize.valueSize) / 2);

        var settings = (IDatabaseSettings)_sealedSettings;
        var directories = Directory.GetDirectories(_sealedSettings.DatabaseFolder, "SensorValues*").Sum(DirectorySize);
        var sealedDirectory = Directory.GetDirectories(_sealedSettings.DatabaseFolder, $"{SealedWeekMarker.DirectoryPrefix}_*").Single();

        Assert.True(DirectorySize(sealedDirectory) > 0);
        Assert.Equal(directories, _sealed.SensorHistoryDbSize);
    }

    private void AssertSameExport(string weekName)
    {
        var plainWeek = _plain.SensorValuesDatabases.Single(db => db.Contains(SealedWeekDay.Ticks)).Name;
        var names = new Dictionary<Guid, string> { [_dense] = "dense", [_bar] = "bar", [_file] = "file" };

        _plain.ExportValuesDatabase(plainWeek, names);
        _sealed.ExportValuesDatabase(weekName, names);

        static List<string> Head(string folder) => File.ReadAllLines(Directory.GetFiles(folder, "*.csv").Single())
            .Select(line => string.Join(',', line.Split(',').Take(3))).ToList();

        var plainLines = Head(_plainSettings.ExportFolder);
        var sealedLines = Head(_sealedSettings.ExportFolder);

        Assert.True(plainLines.Count > 9_000);
        Assert.Equal(plainLines, sealedLines);
    }

    private static long DirectorySize(string path) => new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}
