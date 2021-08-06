using System;
using HSMDatabase.SensorsDatabase;

namespace HSMDatabase.DatabaseWorkCore
{
    internal interface ITimeDatabaseDictionary
    {
        bool TryGetDatabase(DateTime time, out ISensorsDatabase database);
        void AddDatabase(ISensorsDatabase database);
    }
}