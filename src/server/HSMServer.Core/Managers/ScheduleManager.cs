using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly Dictionary<DateTime, Dictionary<Guid, ScheduleAlertMessage>> _storage = new();

        private readonly object _lock = new object();

        internal void ProcessMessage(AlertMessage message)
        {
            lock (_lock)
            {
                var sendFirstAlerts = new List<AlertResult>(1 << 2);
                var sensorId = message.SensorId;

                var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);

                SendAlertMessage(sensorId, notApplyAlerts);

                foreach (var alert in applyAlerts)
                {
                    if (!_storage.ContainsKey(alert.SendTime))
                        _storage.Add(alert.SendTime, []);

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
        }


        internal override void FlushMessages()
        {
            lock (_lock)
            {
                foreach (var (sendTime, branch) in _storage)
                    if (sendTime < DateTime.UtcNow && _storage.Remove(sendTime, out _))
                    {
                        foreach (var (_, message) in branch)
                            SendAlertMessage(message.FilterMessage());

                        branch.Clear();
                    }
            }
        }
    }
}