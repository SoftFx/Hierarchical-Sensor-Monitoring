using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.SealedHistory;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// A chunk of a sealed week that could not be read: a decode failure of the codec, or a chunk that does not
    /// fit its key or its neighbours. Never caught to return fewer rows; see the sealed history feature doc.
    /// </summary>
    public sealed class SealedChunkReadException : IOException
    {
        public string Database { get; }

        public Guid SensorId { get; }

        public long FirstTick { get; }


        internal SealedChunkReadException(string database, Guid sensorId, long firstTick, string message, Exception inner)
            : base($"Sealed chunk {sensorId}:{firstTick} in {database}: {message}", inner)
        {
            Database = database;
            SensorId = sensorId;
            FirstTick = firstTick;
        }
    }


    /// <summary>
    /// Reads the chunks of one sealed week with the checks the store relies on, and reports every failure
    /// once where it is found: an error in the log, the store's counter and <see cref="SealedHistoryDiagnostics"/>.
    /// </summary>
    internal sealed class SealedChunkReader(string database)
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

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
            _logger.Error(exception, exception.Message);

            return exception;
        }


        public void CheckKey(byte[] chunkKey)
        {
            if (chunkKey.Length != KeyOrder.KeyLength)
                throw Fail(chunkKey, $"a chunk key of {chunkKey.Length} bytes; sensorId | firstTick is {KeyOrder.KeyLength}", null);
        }
    }
}
