using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class SelfMonitoringService : BaseDelayedBackgroundService
    {
        public override TimeSpan Delay { get; }


        public SelfMonitoringService()
        {
            
        }
        
        protected override Task ServiceAction()
        {
            return Task.FromResult(true);
        }
    }
}
