using HSMDatabase.AccessManager;
using System;
using System.Collections.Generic;

namespace HSMDatabase.DatabaseWorkCore
{
    internal interface ITimeDatabaseDictionary
    {
        bool TryGetDatabase(DateTime time, out ISensorsDatabase database);
        void AddDatabase(ISensorsDatabase database);
        List<ISensorsDatabase> GetAllDatabases();
        List<ISensorsDatabase> GetSortedDatabases();
    }
}