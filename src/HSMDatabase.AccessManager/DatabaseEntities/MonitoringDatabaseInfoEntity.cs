using System;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.Entity
{
    public class MonitoringDatabaseInfoEntity : IMonitoringDatabaseInfoEntity
    {
        public long Id { get; set; }
        public string FolderName { get; set; }
        public DateTime MinDateTime { get; set; }
        public DateTime MaxDateTime { get; set; }
    }
}
