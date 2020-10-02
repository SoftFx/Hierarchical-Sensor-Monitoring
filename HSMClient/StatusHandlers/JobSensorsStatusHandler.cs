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

        public JobSensorsStatusHandler(SensorMonitoringInfo sensorInfo)
        {
            _validationParams = new Dictionary<string, string>();
            _validationParams["warning"] = sensorInfo.WarningPeriod.Ticks.ToString();
            _validationParams["error"] = sensorInfo.ErrorPeriod.Ticks.ToString();
        }
        public void UpdateStatus(MonitoringCounterBaseViewModel counter)
        {
            SensorsService.SensorResponse data = (SensorsService.SensorResponse) counter.DataObject;
            if (!data.Success)
            {
                counter.Status = TextConstants.Error;
                counter.Message = "Task failed!";
                return;
            }

            long diffTicks = DateTime.Now.Ticks - data.Ticks;

            if (diffTicks > long.Parse(_validationParams["error"]))
            {
                counter.Status = TextConstants.Error;
                counter.Message = "Last updated too outdated";
                return;
            }

            if (diffTicks > long.Parse(_validationParams["warning"]))
            {
                counter.Status = TextConstants.Warning;
                counter.Message = "Outdated";
                return;
            }

            counter.Status = TextConstants.Ok;
            counter.Message = "";
        }
    }
}
