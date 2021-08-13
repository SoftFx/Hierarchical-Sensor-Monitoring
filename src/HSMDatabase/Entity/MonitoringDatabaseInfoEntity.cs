using System;

namespace HSMDatabase.Entity
{
    public class MonitoringDatabaseInfoEntity
    {
        public long Id { get; set; }
        public string FolderName { get; set; }
        public DateTime MinDateTime { get; set; }
        public DateTime MaxDateTime { get; set; }
    }
}
