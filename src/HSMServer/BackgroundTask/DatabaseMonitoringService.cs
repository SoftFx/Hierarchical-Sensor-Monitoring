using HSM.Core.Monitoring;
using HSMServer.Core.DataLayer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public class DatabaseMonitoringService : BackgroundService
    {
        private DateTime _lastReported;
        private readonly TimeSpan _checkInterval = new TimeSpan(0,0, 5, 0);
        private readonly ILogger<DatabaseMonitoringService> _logger;
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly IDataCollectorFacade _dataCollector;

        public DatabaseMonitoringService(IDatabaseAdapter databaseAdapter, IDataCollectorFacade dataCollector,
            ILogger<DatabaseMonitoringService> logger)
        {
            _databaseAdapter = databaseAdapter;
            _dataCollector = dataCollector;
            _logger = logger;
            _logger.LogInformation("Database monitoring service initialized");
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do
            {
                _dataCollector.ReportDatabaseSize(_databaseAdapter.GetDatabaseSize());
                _dataCollector.ReportMonitoringDataSize(_databaseAdapter.GetMonitoringDataSize());
                _dataCollector.ReportEnvironmentDataSize(_databaseAdapter.GetEnvironmentDatabaseSize());

                await Task.Delay(_checkInterval, stoppingToken);

            } while (!stoppingToken.IsCancellationRequested);
        }
    }
}
