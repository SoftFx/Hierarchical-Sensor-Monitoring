using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly CTimeDict<CTimeDict<ScheduleAlertMessage>> _storage = new();
        private readonly TimeSpan _grouppingPeriod = TimeSpan.FromHours(1);


        internal void ReceiveNewAlerts(Guid sensorId, List<AlertResult> totalAlerts)
        {
            var utcTime = DateTime.UtcNow;
            var (notApplyAlerts, applyAlerts) = totalAlerts.SplitByCondition(u => u.SendTime <= utcTime);

            ThrowAlertResults(sensorId, notApplyAlerts);

            foreach (var alert in applyAlerts)
            {
                var grouppingDate = alert.BuildDate.Floor(_grouppingPeriod);
                var grouppedAlerts = _storage[alert.SendTime];

                if (!grouppedAlerts.ContainsKey(grouppingDate))
                    grouppedAlerts.TryAdd(grouppingDate, new ScheduleAlertMessage(alert.BuildDate));

                grouppedAlerts[grouppingDate].Alerts.Add(alert);
            }
        }
    }
}