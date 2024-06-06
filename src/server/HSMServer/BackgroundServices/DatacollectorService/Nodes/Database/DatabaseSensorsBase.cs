using HSMDataCollector.Core;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using System;

namespace HSMServer.BackgroundServices
{
    public abstract class DatabaseSensorsBase(IDataCollector collector, IDatabaseCore database, IServerConfig config)
    {
        private const double MbDivisor = 1 << 20;
        private const int DigitsCnt = 2;

        protected const string NodeName = "Database";

        protected readonly IDataCollector _collector = collector;
        protected readonly IServerConfig _serverConfig = config;
        protected readonly IDatabaseCore _database = database;


        internal abstract void SendInfo();


        public static double GetRoundedDouble(long sizeInBytes)
        {
            return Math.Round(sizeInBytes / MbDivisor, DigitsCnt, MidpointRounding.AwayFromZero);
        }
    }
}
