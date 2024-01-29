using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HSMDatabase.DatabaseWorkCore;

internal sealed class JournalValuesDatabaseDictionary : IEnumerable<IJournalValuesDatabase>
{
    private readonly ConcurrentQueue<IJournalValuesDatabase> _sensorDbs = new();
    private readonly IDatabaseSettings _dbSettings;

    private IJournalValuesDatabase _lastDb;

    private string DataBasePath => _dbSettings.PathToJournalDb;


    internal JournalValuesDatabaseDictionary(IDatabaseSettings dbSettings)
    {
        _dbSettings = dbSettings;

        if (!Directory.Exists(DataBasePath))
            Directory.CreateDirectory(DataBasePath);

        var journalValuesDirectories = GetJournalValuesDirectories();
        foreach (var directory in journalValuesDirectories)
        {
            var (from, to) = GetDatesFromFolderName(directory);
            AddNewDb(directory, from, to);
        }
    }


    internal IJournalValuesDatabase GetNewestDatabases(long time)
    {
        if (_lastDb == null || _lastDb.To < time)
        {
            var from = DateTimeMethods.GetMinDateTimeTicks(time);
            var to = DateTimeMethods.GetMaxDateTimeTicks(time);

            return AddNewDb(_dbSettings.GetPathToJournalValueDatabase(from, to), from, to);
        }

        return _lastDb;
    }

    internal IJournalValuesDatabase AddNewDb(string name, long from, long to)
    {
        _lastDb = LevelDBManager.GetJournalValuesDatabaseInstance(name, from, to);

        _sensorDbs.Enqueue(_lastDb);

        return _lastDb;
    }

    private List<string> GetJournalValuesDirectories()
    {
        var journalValuesDirectories = Directory.GetDirectories(DataBasePath, $"{_dbSettings.JournalValuesDatabaseName}*", SearchOption.TopDirectoryOnly);

        return journalValuesDirectories.OrderBy(d => d).ToList();
    }

    private static (long from, long to) GetDatesFromFolderName(string folder)
    {
        var from = 0L;
        var to = 0L;

        var splitResults = folder.Split('_');

        if (long.TryParse(splitResults[1], out long fromTicks))
            from = fromTicks;

        if (long.TryParse(splitResults[2], out long toTicks))
            to = toTicks;

        return (from, to);
    }

    public IEnumerator<IJournalValuesDatabase> GetEnumerator() => _sensorDbs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}