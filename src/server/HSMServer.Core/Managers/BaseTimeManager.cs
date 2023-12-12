using HSMServer.Core.Model.Policies;
using NLog;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    internal abstract class BaseTimeManager
    {
        protected readonly Logger _logger = LogManager.GetCurrentClassLogger();


        internal event Action<Guid, List<AlertResult>> ThrowAlertResultsEvent;


        protected void ThrowAlertResults(Guid sensorId, List<AlertResult> alertResults)
        {
            if (alertResults.Count > 0)
                ThrowAlertResultsEvent?.Invoke(sensorId, alertResults);
        }
    }
}