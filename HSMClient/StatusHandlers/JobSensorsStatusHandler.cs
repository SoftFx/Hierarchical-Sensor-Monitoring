using System;
using System.Collections.Generic;
using HSMClient.Common;
using HSMClient.Configuration;
using HSMClientWPFControls;
using HSMClientWPFControls.ViewModel;
using HSMCommon.DataObjects;

namespace HSMClient.StatusHandlers
{
    class JobSensorsStatusHandler : IMonitoringCounterStatusHandler
    {
        private Dictionary<string, string> _validationParams;


        public void UpdateStatus(MonitoringCounterBaseViewModel counter)
        {
            counter.Status = TextConstants.Ok;
        }
    }
}
