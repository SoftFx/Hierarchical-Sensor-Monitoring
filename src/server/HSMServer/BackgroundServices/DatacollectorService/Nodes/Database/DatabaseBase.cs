using HSMDataCollector.Core;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration.Monitoring;
using Microsoft.Extensions.Options;

namespace HSMServer.BackgroundServices
{
    public abstract class DatabaseBase(IDataCollector collector, IDatabaseCore database, IOptionsMonitor<MonitoringOptions> optionsMonitor)
    {
        protected const string NodeName = "Database";

        protected readonly IDataCollector _collector = collector;
        protected readonly IDatabaseCore _database = database;
        protected readonly IOptionsMonitor<MonitoringOptions> _optionsMonitor = optionsMonitor;


        internal abstract void SendInfo();
    }
}
