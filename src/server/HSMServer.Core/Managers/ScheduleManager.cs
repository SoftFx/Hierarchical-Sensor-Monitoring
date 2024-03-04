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
            var sensorId = message.SensorId;

            var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);

            var sendFirstAlerts = new List<AlertResult>(1 << 2);
            foreach (var alert in applyAlerts)
            {
                var grouppedAlerts = _storage[alert.SendTime];

                if (!grouppedAlerts.TryGetValue(sensorId, out var sensorGroup))
                {
                    sensorGroup = new ScheduleAlertMessage(sensorId, alert.PolicyId);
                    grouppedAlerts.TryAdd(sensorId, sensorGroup);
                    sendFirstAlerts.Add(alert);
                }

                if (alert.IsReplaceAlert)
                    sensorGroup.RemovePolicyAlerts(alert.PolicyId);

                sensorGroup.AddAlert(alert);
            }
            
            SendAlertMessage(sensorId, notApplyAlerts);
            SendAlertMessage(sensorId, sendFirstAlerts);
        }


        internal override void FlushMessages()
        {
            foreach (var (sendTime, branch) in _storage)
                if (sendTime < DateTime.UtcNow && _storage.TryRemove(sendTime, out _))
                {
                    foreach (var (_, message) in branch)
                    {
                        if (!message.ShouldSend(message.PolicyId))
                            SendAlertMessage(message);
                    }

                    branch.Clear();
                }
        }
    }
}