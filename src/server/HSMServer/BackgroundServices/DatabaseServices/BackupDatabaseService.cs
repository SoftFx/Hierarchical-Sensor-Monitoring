using HSMServer.ServerConfiguration;
using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    public class BackupDatabaseService : BaseDelayedBackgroundService
    {
        public override TimeSpan Delay { get; }


        public BackupDatabaseService(IServerConfig config)
        {
            Delay = TimeSpan.FromHours(config.BackupDatabase.PeriodHours);
        }


        protected override Task ServiceAction()
        {
            return Task.CompletedTask;
        }
    }
}
