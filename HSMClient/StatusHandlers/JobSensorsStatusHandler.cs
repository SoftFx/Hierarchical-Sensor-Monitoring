using System.Collections.Generic;
using HSMClient.Common;
using HSMClientWPFControls;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.StatusHandlers
{
    class JobSensorsStatusHandler : IMonitoringSensorStatusHandler
    {
        private Dictionary<string, string> _validationParams;


        public void UpdateStatus(MonitoringSensorViewModel sensor)
        {
            sensor.Status = TextConstants.Ok;
        }
    }
}
