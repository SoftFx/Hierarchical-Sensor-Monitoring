using System;
using System.IO;

namespace HSMDatabase.AccessManager
{
    /// <summary>
    /// A chunk of a sealed week that could not be read (#1803): a decode failure of the codec, or a chunk that does
    /// not fit its key or its neighbours. Thrown by the sealed week store instead of returning fewer rows or "no
    /// value"; already logged and counted (<see cref="SealedHistoryDiagnostics"/>) when a caller sees it.
    /// </summary>
    public sealed class SealedChunkReadException : IOException
    {
        public string Database { get; }

        public Guid SensorId { get; }

        public long FirstTick { get; }


        public SealedChunkReadException(string database, Guid sensorId, long firstTick, string message, Exception inner = null)
            : base($"Sealed chunk {sensorId}:{firstTick} in {database}: {message}", inner)
        {
            Database = database;
            SensorId = sensorId;
            FirstTick = firstTick;
        }
    }
}
