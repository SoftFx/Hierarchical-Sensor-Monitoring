using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.SealedHistory;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// Reads the chunks of one sealed week with the checks the store relies on, and reports every failure
    /// once where it is found: an error in the log, the store's counter and <see cref="SealedHistoryDiagnostics"/>.
    /// </summary>
    internal sealed class SealedChunkReader(string database)
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // The chunks already logged as errors in this process, by database and chunk key. Bounded by the number
        // of corrupt chunks, not by the number of reads.
        private static readonly ConcurrentDictionary<string, byte> _loggedChunks = new();

        private long _failures;


        public long Failures => Interlocked.Read(ref _failures);


        public ChunkHeader ReadHeader(byte[] chunkKey, byte[] chunk)
        {
            CheckKey(chunkKey);

            ChunkHeader header;

            try
            {
                header = ChunkCodec.ReadHeader(chunk);
            }
            catch (ChunkFormatException e)
            {
                throw Fail(chunkKey, e.Message, e);
            }

            if (header.FirstKey != KeyOrder.Tick(chunkKey))
                throw Fail(chunkKey, $"the header's first key {header.FirstKey} is not the key it is stored under", null);

            return header;
        }

        public List<HistoryRow> Decode(byte[] chunkKey, byte[] chunk)
        {
            CheckKey(chunkKey);

            List<HistoryRow> rows;

            try
            {
                rows = ChunkCodec.Decode(chunk);
            }
            catch (ChunkFormatException e)
            {
                throw Fail(chunkKey, e.Message, e);
            }

            if (rows[0].Key != KeyOrder.Tick(chunkKey))
                throw Fail(chunkKey, $"the first row {rows[0].Key} is not the key the chunk is stored under", null);

            // Row keys are ticks of the storage key, which orders them bytewise as unsigned big-endian numbers.
            if (rows[0].Key < 0)
                throw Fail(chunkKey, "a row key is negative", null);

            return rows;
        }

        public SealedChunkReadException Fail(byte[] chunkKey, string message, Exception inner)
        {
            Guid sensorId = Guid.Empty;
            long firstTick = 0;

            if (chunkKey is { Length: KeyOrder.KeyLength })
            {
                sensorId = new Guid(chunkKey.AsSpan(0, KeyOrder.SensorIdLength));
                firstTick = KeyOrder.Tick(chunkKey);
            }

            var exception = new SealedChunkReadException(database, sensorId, firstTick, message, inner);

            Interlocked.Increment(ref _failures);
            SealedHistoryDiagnostics.CountChunkReadFailure();

            // Every failure counts; a chunk is an error in the log once per process. A corrupt chunk stays corrupt,
            // and the callers that retry (the sensor history load, retention) would otherwise repeat it forever.
            var chunk = $"{database}|{(chunkKey is null ? "?" : Convert.ToHexString(chunkKey))}";
            if (_loggedChunks.TryAdd(chunk, 0))
                _logger.Error(exception, exception.Message + " (logged once per process; later failures of this chunk are counted only)");
            else
                _logger.Debug(exception.Message);

            return exception;
        }


        public void CheckKey(byte[] chunkKey)
        {
            if (chunkKey.Length != KeyOrder.KeyLength)
                throw Fail(chunkKey, $"a chunk key of {chunkKey.Length} bytes; sensorId | firstTick is {KeyOrder.KeyLength}", null);
        }
    }
}
