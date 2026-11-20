using HSMDatabase.AccessManager;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1805 follow-up, review R1: before hsm-sealed the seal makes the overlay independent of its write-ahead logs
// (LevelDBDatabaseAdapter.FlushMemTablesToTables), so a power loss cannot take back a row whose chunk row exists. Checked
// against the native LevelDB actually bundled, on each platform: rows written without sync, then the step, then a copy of
// the directory in which every write-ahead log is truncated to zero bytes (what an unsynced log can be after a power
// loss) is opened, and every row must be there; the tables written before the step must be untouched.
public sealed class MemTableFlushTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-1805f-{Guid.NewGuid():N}");


    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    public enum Step { None, EmptySyncedBatch, Flush }

    // rows: 2,000 (about 2 MB) stay in the current memtable; 9,500 (about 9.5 MB, over the 8 MB write buffer) switch the
    // memtable right before the step, so one is immutable or being flushed in the background when the step runs.
    [Theory]
    [InlineData(2_000, Step.Flush, true)]
    [InlineData(9_500, Step.Flush, true)]
    [InlineData(2_000, Step.None, false)]
    [InlineData(9_500, Step.None, false)]
    [InlineData(2_000, Step.EmptySyncedBatch, false)]
    [InlineData(9_500, Step.EmptySyncedBatch, false)]
    public void AfterTheFlush_EveryRowSurvivesTheLossOfEveryLog_AndNoTableIsRewritten(int rows, Step step, bool expectEveryRow)
    {
        var path = Path.Combine(_root, "SensorValuesV2_1_2");
        var sensor = new Guid("6c1d0000-0000-4000-8000-000000000001");
        var random = new Random(rows);

        Dictionary<string, long> tablesBefore;

        using (var db = new LevelDBDatabaseAdapter(path))
        {
            // Rows already in tables, as in a week the seal finds.
            for (int i = 0; i < 3_000; i++)
                db.Put(new DbKey(sensor, i).ToBytes(), Value(random));

            db.Compact();
            tablesBefore = Tables(path);
            Assert.NotEmpty(tablesBefore);

            // The rows at stake: written without sync, as the server writes.
            for (int i = 0; i < rows; i++)
                db.Put(new DbKey(sensor, 1_000_000 + i).ToBytes(), Value(random));

            var watch = System.Diagnostics.Stopwatch.StartNew();

            switch (step)
            {
                case Step.Flush:
                    db.FlushMemTablesToTables();
                    break;
                case Step.EmptySyncedBatch:
                    using (var empty = new global::LevelDB.WriteBatch())
                        db.Native.Write(empty, new global::LevelDB.WriteOptions { Sync = true });
                    break;
            }

            output.WriteLine($"{step} with {rows} unsynced rows: {watch.Elapsed.TotalMilliseconds:F1} ms");

            // Every table written before the step is still there, with its size: nothing was rewritten.
            var tablesAfter = Tables(path);
            foreach (var (name, size) in tablesBefore)
                Assert.True(tablesAfter.TryGetValue(name, out var after) && after == size, $"table {name} was rewritten or deleted");
        }

        // The image of a power loss that took every write-ahead log.
        var image = Path.Combine(_root, "image", "SensorValuesV2_1_2");
        Directory.CreateDirectory(image);

        foreach (var file in Directory.GetFiles(path))
            if (Path.GetFileName(file) != "LOCK")
                File.Copy(file, Path.Combine(image, Path.GetFileName(file)));

        var logs = Directory.GetFiles(image, "*.log").Where(f => Path.GetFileName(f)[..^4].All(char.IsAsciiDigit)).ToList();
        foreach (var log in logs)
            File.WriteAllBytes(log, []);

        using var reopened = new LevelDBDatabaseAdapter(image);
        var count = reopened.GetAll().Count(kv => DbKey.FromBytes(kv.Item1).Timestamp >= 1_000_000);

        output.WriteLine($"{logs.Count} logs truncated; {count} of {rows} rows survived");

        if (expectEveryRow)
            Assert.Equal(rows, count);
        else
            Assert.True(count < rows, $"all {rows} rows survived without the flush: the test does not tell the steps apart");
    }

    private static byte[] Value(Random random)
    {
        var value = new byte[1024];
        random.NextBytes(value);
        return value;
    }

    private static Dictionary<string, long> Tables(string path) =>
        new DirectoryInfo(path).GetFiles().Where(f => f.Extension is ".ldb" or ".sst").ToDictionary(f => f.Name, f => f.Length);
}
