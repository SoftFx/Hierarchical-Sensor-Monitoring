using HSM.Core.Monitoring;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    /// <summary>
    /// Background service, which reports database size every 5 minutes 
    /// </summary>
    public class DatabaseMonitoringService : BackgroundService
    {
        private readonly TimeSpan _checkInterval = new TimeSpan(0, 0, 5, 0);
        
        private readonly ILogger<DatabaseMonitoringService> _logger;
        
        private readonly IDatabaseCore _databaseCore;
        
        private readonly IDataCollectorFacade _dataCollector;

        public DatabaseMonitoringService(IDatabaseCore databaseCore, IDataCollectorFacade dataCollector,
            ILogger<DatabaseMonitoringService> logger)
        {
            _databaseCore = databaseCore;
            _dataCollector = dataCollector;
            _logger = logger;
            _logger.LogInformation("Database monitoring service initialized");
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                _dataCollector.ReportDatabaseSize(_databaseCore.GetDatabaseSize());
                _dataCollector.ReportSensorsHistoryDataSize(_databaseCore.GetSensorsHistoryDatabaseSize());
                _dataCollector.ReportEnvironmentDataSize(_databaseCore.GetEnvironmentDatabaseSize());

                await Task.Delay(_checkInterval, stoppingToken);

            } while (!stoppingToken.IsCancellationRequested);
        }
    }
}
