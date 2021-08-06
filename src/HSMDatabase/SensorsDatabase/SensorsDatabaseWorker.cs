using System;

namespace HSMDatabase.SensorsDatabase
{
    internal class SensorsDatabaseWorker : ISensorsDatabase
    {
        private readonly DateTime _databaseMinTime;
        private readonly DateTime _databaseMaxTime;
        public long DatabaseMinTicks => _databaseMinTime.Ticks;
        public long DatabaseMaxTicks => _databaseMaxTime.Ticks;
        private readonly string _name;

        public SensorsDatabaseWorker(string name, DateTime minTime, DateTime maxTime)
        {
            _databaseMinTime = minTime;
            _databaseMaxTime = maxTime;
            _name = name;
        }
    }
}