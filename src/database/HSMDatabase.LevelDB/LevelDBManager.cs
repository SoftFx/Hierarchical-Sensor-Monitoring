using System;
using System.IO;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.DatabaseImplementations;
using NLog;

namespace HSMDatabase.LevelDB
{
    public static class LevelDBManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public static IEnvironmentDatabase GetEnvitonmentDatabaseInstance(string name) =>
            new EnvironmentDatabaseWorker(name);

        public static ISensorValuesDatabase GetSensorValuesDatabaseInstance(string name, long from, long to) =>
            new SensorValuesDatabaseWorker(name, from, to);

        /// <summary>
        /// The history of one week: a sealed week (ADR-0020) when <paramref name="sealedName"/> holds a complete
        /// sealed database (<see cref="SealedWeekMarker"/>), otherwise the row database alone. A sealed directory
        /// without the marker is an interrupted or running seal and is not read.
        /// </summary>
        // utcNow: the clock a sealed week asks whether its overlay holds every row (#1805 follow-up); null: the system clock.
        public static ISensorValuesDatabase GetSensorValuesDatabaseInstance(string name, string sealedName, long from, long to, DateTime? utcNow = null)
        {
            if (SealedWeekMarker.ExistsIn(sealedName))
                return new SealedSensorValuesDatabase(name, sealedName, from, to, utcNow: utcNow);

            if (Directory.Exists(sealedName))
                _logger.Warn($"{sealedName} has no {SealedWeekMarker.FileName} marker: an incomplete seal, not read; the week is served from {name} alone");

            return new SensorValuesDatabaseWorker(name, from, to);
        }

        // utcNow: the clock the shard records its last reclaimable change by (#1801); null: the system clock.
        public static IJournalValuesDatabase GetJournalValuesDatabaseInstance(string name, long from, long to, Func<DateTime> utcNow = null) =>
            new JournalValuesDatabaseWorker(name, from, to) { Clock = utcNow };


        public static IEntityDatabase GetEntityDatabase(string name) =>
            new LevelDBDatabaseAdapter(name);
    }
}
