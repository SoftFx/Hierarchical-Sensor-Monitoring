using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager;

namespace HSMDatabase.DatabaseWorkCore
{
    internal interface ITimeDatabaseDictionary
    {
        bool TryGetDatabase(DateTime time, out ISensorsDatabase database);
        ISensorsDatabase GetDatabase(DateTime time);
        void AddDatabase(ISensorsDatabase database);
        List<ISensorsDatabase> GetAllDatabases();
    }
}