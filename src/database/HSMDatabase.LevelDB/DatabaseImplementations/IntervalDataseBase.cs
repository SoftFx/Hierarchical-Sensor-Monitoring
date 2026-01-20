using System;
using NLog;


namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal abstract class IntervalDataseBase : IDisposable
    {

        protected readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected readonly LevelDBDatabaseAdapter _openedDb;

        public string Name { get; }

        public long From { get; }

        public long To { get; }

        public bool Contains(long time) => From <= time && time < To;

        public bool Overlaps(long from, long to) => From < to && To > from;


        public IntervalDataseBase(string name, long from, long to)
        {
            _openedDb = new LevelDBDatabaseAdapter(name);

            Name = name;
            From = from;
            To = to;
        }

        public void Dispose() => _openedDb.Dispose();

    }
}
