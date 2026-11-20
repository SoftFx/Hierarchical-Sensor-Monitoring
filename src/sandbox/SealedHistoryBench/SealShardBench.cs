using System.Diagnostics;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using MemoryPack;

namespace SealedHistoryBench.Strings
{
    /// <summary>
    /// Seals a whole shard with the production codec, streaming sensor by sensor with memory bounded by one sensor
    /// (a sensor above <c>--max-sensor-rows</c> is counted in the main pass, then re-read chunk by chunk). Every chunk
    /// is decoded with <see cref="ChunkCodec.Decode(ReadOnlySpan{byte})"/> and every row compared by
    /// <see cref="RowFidelity"/> (Comment and String values included). Also measures how far the encoder's string
    /// column choice (smaller before zstd) is from the best choice after zstd. Prints numbers only, never an id, a
    /// path or any string content.
    ///
    ///   SealedHistoryBench --seal-shard &lt;shard-copy&gt; [--max-sensor-rows N]
    /// </summary>
    internal sealed class SealShardBench
    {
        private readonly Dictionary<SensorType, (long Sensors, long Rows, long Source, long Stored, long Chunks)> _byType = new();
        private readonly Dictionary<string, long> _counts = new();
        private readonly Dictionary<SensorType, (int Sensors, long Rows)> _refused = new();
        private double _encodeMs, _decodeMs;
        private long _sensorsDone;


        public static void Run(string shardPath, int maxSensorRows)
        {
            var bench = new SealShardBench();
            var clock = Stopwatch.StartNew();
            var seen = new HashSet<Guid>();
            long rowsRead = 0, otherKeys = 0, nextProgress = 1_000_000, overflowSensors = 0;

            using var db = LevelDBDatabaseAdapter.ForReadOnly(Path.GetFullPath(shardPath));

            Guid current = default;
            bool has = false, overflow = false;
            long previousTimestamp = 0, sensorSource = 0, sensorRows = 0;
            var buffer = new List<HistoryRow>();

            void Flush()
            {
                if (!has)
                    return;

                if (!overflow)
                    bench.Sensor(buffer.Count, sensorSource, Split(buffer, buffer.Count));
                else
                {
                    overflowSensors++;
                    bench.Sensor(sensorRows, sensorSource, Split(Range(db, current), sensorRows));
                }

                buffer = new List<HistoryRow>();
            }

            foreach (var (key, value) in db.GetAll())
            {
                if (key.Length != 24)
                {
                    otherKeys++;
                    continue;
                }

                var dbKey = DbKey.FromBytes(key);

                if (!has || dbKey.SensorId != current)
                {
                    Flush();
                    if (!seen.Add(dbKey.SensorId))
                        throw new InvalidOperationException("the shard does not iterate sensor by sensor");
                    current = dbKey.SensorId;
                    has = true;
                    overflow = false;
                    sensorSource = sensorRows = 0;
                }
                else if (dbKey.Timestamp <= previousTimestamp)
                    throw new InvalidOperationException("keys of one sensor are not strictly ascending");

                previousTimestamp = dbKey.Timestamp;
                sensorRows++;
                sensorSource += value.Length;

                if (!overflow)
                {
                    buffer.Add(new HistoryRow(dbKey.Timestamp, MemoryPackSerializer.Deserialize<BaseValue>(value)));
                    if (buffer.Count > maxSensorRows)
                    {
                        overflow = true;
                        buffer = new List<HistoryRow>();
                    }
                }

                if (++rowsRead >= nextProgress)
                {
                    nextProgress += 1_000_000;
                    Console.Error.WriteLine($"[progress] rows {rowsRead:N0}, sensors done {bench._sensorsDone:N0}, elapsed {clock.Elapsed.TotalSeconds:F0} s, working set {Environment.WorkingSet / (1024 * 1024):N0} MB");
                }
            }

            Flush();

            Report.Line("== SEAL SHARD (production codec, streamed)");
            Report.Line($"rows {rowsRead:N0}, sensors {seen.Count:N0}, other keys {otherKeys}, sensors streamed chunk by chunk (more than {maxSensorRows:N0} rows): {overflowSensors}");
            bench.Print();
            Report.Line($"elapsed {clock.Elapsed.TotalSeconds:F0} s, peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024):F0} MB");
        }


        private void Sensor(long rowCount, long sourceBytes, IEnumerable<List<HistoryRow>> chunks)
        {
            _sensorsDone++;
            SensorType type = default;
            long stored = 0, chunkCount = 0;
            var local = new Dictionary<string, long>();
            double encode = 0, decode = 0;

            foreach (var rows in chunks)
            {
                if (chunkCount == 0)
                    type = rows[0].Value.Type;

                var sw = Stopwatch.StartNew();
                var result = ChunkCodec.Encode(rows);
                encode += sw.Elapsed.TotalMilliseconds;

                if (!result.IsSuccess || result.Chunks.Count != 1 || rows.Any(r => r.Value.Type != type))
                {
                    var r = _refused.GetValueOrDefault(type);
                    _refused[type] = (r.Sensors + 1, r.Rows + rowCount);
                    return;
                }

                var chunk = result.Chunks[0];
                sw.Restart();
                var decoded = ChunkCodec.Decode(chunk);
                decode += sw.Elapsed.TotalMilliseconds;

                Add(local, "rows checked", rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    if (RowFidelity.FindDifference(rows[i], decoded[i]) is not null)
                        Add(local, "mismatches", 1);

                Choices(rows, type, chunk.Length, local);

                stored += chunk.Length;
                chunkCount++;
            }

            if (chunkCount == 0)
                return;

            var t = _byType.GetValueOrDefault(type);
            _byType[type] = (t.Sensors + 1, t.Rows + rowCount, t.Source + sourceBytes, t.Stored + stored, t.Chunks + chunkCount);
            foreach (var (k, v) in local)
                Add(_counts, k, v);
            _encodeMs += encode;
            _decodeMs += decode;
        }

        /// <summary>
        /// The string column modes the encoder chose, and the stored size of every combination of dictionary and
        /// template for the chunk's non-constant string columns: how far "smaller before zstd" is from best after zstd.
        /// </summary>
        private void Choices(List<HistoryRow> rows, SensorType type, int storedLength, Dictionary<string, long> local)
        {
            var comments = rows.Select(r => r.Value.Comment).ToArray();
            var values = type == SensorType.String ? rows.Select(r => ((StringValue)r.Value).Value).ToArray() : null;

            byte commentMode = StringColumn.ChooseMode(comments);
            Add(local, $"Comment mode {commentMode}", 1);
            byte valueMode = values is null ? (byte)255 : StringColumn.ChooseMode(values);
            if (values is not null)
                Add(local, $"String value mode {valueMode}", 1);

            var commentOptions = Options(comments, commentMode);
            var valueOptions = values is null ? [null] : Options(values, valueMode);
            int best = int.MaxValue;

            foreach (var commentWriter in commentOptions)
                foreach (var valueWriter in valueOptions)
                {
                    var sink = new ByteSink(rows.Count * 16, ChunkCodec.MaxPayloadBytes);
                    VariantPayloadWriter.Write(sink, rows, 0, rows.Count, type,
                        commentWriter ?? ((s, v) => StringColumn.Write(s, v)), valueWriter ?? ((s, v) => StringColumn.Write(s, v)));
                    best = Math.Min(best, ChunkCodec.Assemble(sink.Written, type, rows.Count, rows[0].Key, rows[^1].Key).Length);
                }

            Add(local, "stored as encoded", storedLength);
            Add(local, "stored best of choices", best);
            if (type == SensorType.String)
            {
                Add(local, "String stored as encoded", storedLength);
                Add(local, "String stored best of choices", best);
            }
            if (best < storedLength)
                Add(local, "chunks where another choice is smaller", 1);
        }

        /// <summary>The writers to try for one column: the encoder's own when constant, else dictionary and (when it qualifies) template.</summary>
        private static Action<ByteSink, string[]>[] Options(string[] column, byte chosen)
        {
            if (chosen == StringColumn.ModeConstant)
                return [null];

            var options = new List<Action<ByteSink, string[]>> { (s, v) => StringColumn.WriteDictionary(s, v) };
            if (StringTemplate.TryWrite(new ByteSink(column.Length * 4), column))
                options.Add((s, v) => StringTemplate.TryWrite(s, v));
            return options.ToArray();
        }

        private void Print()
        {
            long rows = _byType.Values.Sum(v => v.Rows), stored = _byType.Values.Sum(v => v.Stored);

            Report.Line($"{"type",-11} {"sensors",8} {"rows",12} {"source B",15} {"stored B",13} {"B/row",7} {"share",7}");
            foreach (var (type, v) in _byType.OrderBy(kv => kv.Key))
                Report.Line($"{type,-11} {v.Sensors,8:N0} {v.Rows,12:N0} {v.Source,15:N0} {v.Stored,13:N0} {(double)v.Stored / v.Rows,7:F3} {100.0 * v.Stored / stored,6:F2}%");
            Report.Line($"{"total",-11} {_byType.Values.Sum(v => v.Sensors),8:N0} {rows,12:N0} {_byType.Values.Sum(v => v.Source),15:N0} {stored,13:N0} {(double)stored / rows,7:F3}");
            foreach (var (type, (n, r)) in _refused)
                Report.Line($"refused (not sealed, excluded above): {type} {n} sensors, {r:N0} rows");

            Report.Line($"chunks {_byType.Values.Sum(v => v.Chunks):N0}; rows decoded and compared {Get("rows checked"):N0}; mismatches {Get("mismatches")}");
            Report.Line($"Comment column mode per chunk: constant {Get("Comment mode 0"):N0}, dictionary {Get("Comment mode 1"):N0}, template {Get("Comment mode 2"):N0}");
            Report.Line($"String value column mode per chunk: constant {Get("String value mode 0"):N0}, dictionary {Get("String value mode 1"):N0}, template {Get("String value mode 2"):N0}");
            Report.Line($"encode {_encodeMs / 1000:F1} s ({_encodeMs * 1000 / rows:F2} us/row), decode {_decodeMs / 1000:F1} s ({_decodeMs * 1000 / rows:F3} us/row)");
            Report.Line($"string column choice: stored as encoded {Get("stored as encoded"):N0} B, best of {{dictionary, template}} per column after zstd {Get("stored best of choices"):N0} B " +
                        $"(difference {Get("stored as encoded") - Get("stored best of choices"):N0} B, {100.0 * (Get("stored as encoded") - Get("stored best of choices")) / Get("stored as encoded"):F3}%); " +
                        $"String sensors {Get("String stored as encoded"):N0} vs {Get("String stored best of choices"):N0} B; chunks where another choice is smaller {Get("chunks where another choice is smaller"):N0}");
        }

        private long Get(string key) => _counts.GetValueOrDefault(key);

        private static void Add(Dictionary<string, long> counts, string key, long value) => counts[key] = counts.GetValueOrDefault(key) + value;

        private static IEnumerable<List<HistoryRow>> Split(IEnumerable<HistoryRow> rows, long count)
        {
            if (count == 0)
                yield break;

            long chunkCount = (count + ChunkCodec.MaxRowsPerChunk - 1) / ChunkCodec.MaxRowsPerChunk;
            long baseSize = count / chunkCount, larger = count % chunkCount;
            int c = 0;
            long target = baseSize + (c < larger ? 1 : 0), seen = 0;
            var chunk = new List<HistoryRow>((int)target);

            foreach (var row in rows)
            {
                chunk.Add(row);
                seen++;
                if (chunk.Count == target)
                {
                    yield return chunk;
                    c++;
                    target = baseSize + (c < larger ? 1 : 0);
                    chunk = new List<HistoryRow>((int)target);
                }
            }

            if (seen != count || chunk.Count != 0)
                throw new InvalidOperationException($"sensor stream gave {seen} rows, {count} counted");
        }

        private static IEnumerable<HistoryRow> Range(LevelDBDatabaseAdapter db, Guid sensor)
        {
            var from = new byte[24];
            var to = new byte[24];
            sensor.TryWriteBytes(from);
            sensor.TryWriteBytes(to);
            to.AsSpan(16).Fill(0xFF);

            foreach (var (key, value) in db.GetValueKeyPairFromTo(from, to))
            {
                if (key.Length != 24)
                    continue;
                var dbKey = DbKey.FromBytes(key);
                if (dbKey.SensorId == sensor)
                    yield return new HistoryRow(dbKey.Timestamp, MemoryPackSerializer.Deserialize<BaseValue>(value));
            }
        }
    }
}
