using HSMDataCollector.Core;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;
using System;

namespace HSMServer.BackgroundServices
{
    public abstract class DatabaseSensorsBase(IDataCollector collector, IDatabaseCore database, IOptionsMonitor<MonitoringOptions> optionsMonitor)
    {
        private const int DigitsCnt = 2;
        private const double MbDivisor = 1 << 20;
        protected const string NodeName = "Database";

        protected readonly IDataCollector _collector = collector;
        protected readonly IDatabaseCore _database = database;
        protected readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor = optionsMonitor;


        internal abstract void SendInfo();


        protected static double GetRoundedDouble(long sizeInBytes)
        {
            return Math.Round(sizeInBytes / MbDivisor, DigitsCnt, MidpointRounding.AwayFromZero);
        }
    }
}
