using HSMCommon.Collections;
using HSMCommon.Extensions;
using System;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly CTimeDict<CDict<ScheduleAlertMessage>> _storage = new();


        internal void ProcessMessage(AlertMessage message)
        {
            var sensorId = message.SensorId;
            var utcTime = DateTime.UtcNow;

            var (notApplyAlerts, applyAlerts) = message.Alerts.SplitByCondition(u => u.IsScheduleAlert);

            SendAlertMessage(sensorId, notApplyAlerts);

            foreach (var alert in applyAlerts)
            {
                var grouppedAlerts = _storage[alert.SendTime];

                if (!grouppedAlerts.ContainsKey(sensorId))
                    grouppedAlerts.TryAdd(sensorId, new ScheduleAlertMessage(sensorId));

                grouppedAlerts[sensorId].Alerts.Add(alert);
            }
        }


        internal override void FlushMessages()
        {
            foreach (var (sendTime, branch) in _storage)
                if (sendTime < DateTime.UtcNow && _storage.TryRemove(sendTime, out _))
                {
                    foreach (var (_, message) in branch)
                        SendAlertMessage(message);

                    branch.Clear();
                }
        }
    }
}