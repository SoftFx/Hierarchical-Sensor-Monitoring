using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly CTimeDict<CDict<ScheduleAlertMessage>> _storage = new();


        internal void ProcessMessage(AlertMessage message)
        {
            var sendFirstAlerts = new List<AlertResult>(1 << 2);
            var sensorId = message.SensorId;

            var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);

            SendAlertMessage(sensorId, notApplyAlerts);

            foreach (var alert in applyAlerts)
            {
                var grouppedAlerts = _storage[alert.SendTime];

                if (!grouppedAlerts.TryGetValue(sensorId, out var sensorGroup))
                {
                    sensorGroup = new ScheduleAlertMessage(sensorId);
                    grouppedAlerts.TryAdd(sensorId, sensorGroup);
                }

                if (sensorGroup.ShouldSendFirstMessage(alert))
                    sendFirstAlerts.Add(alert);

                sensorGroup.AddAlert(alert);
            }

            SendAlertMessage(sensorId, sendFirstAlerts);
        }


        internal override void FlushMessages()
        {
            foreach (var (sendTime, branch) in _storage)
                if (sendTime < DateTime.UtcNow && _storage.TryRemove(sendTime, out _))
                {
                    foreach (var (_, message) in branch)
                        SendAlertMessage(message.FilterMessage());

                    branch.Clear();
                }
        }
    }
}