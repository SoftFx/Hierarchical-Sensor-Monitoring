using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly CHash<Guid> _sendFirstIds = new();
        private readonly CTimeDict<CDict<ScheduleAlertMessage>> _storage = new();


        internal void ProcessMessage(AlertMessage message)
        {
            var sensorId = message.SensorId;

            var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);

            foreach (var alert in applyAlerts)
            {
                ExtendAlertMessage(alert);
                
                var grouppedAlerts = _storage[alert.SendTime];

                if (!grouppedAlerts.TryGetValue(sensorId, out var sensorGroup))
                {
                    sensorGroup = new ScheduleAlertMessage(sensorId, alert.PolicyId);
                    grouppedAlerts.TryAdd(sensorId, sensorGroup);
                }

                if (alert.IsReplaceAlert)
                    sensorGroup.RemovePolicyAlerts(alert.PolicyId);

                sensorGroup.AddAlert(alert);
            }
            
            SendAlertMessage(sensorId, notApplyAlerts);

            void ExtendAlertMessage(AlertResult alert)
            {
                if (alert.HasScheduleFirstMessage && !_sendFirstIds.Contains(alert.PolicyId))
                {
                    notApplyAlerts.Add(alert);
                    _sendFirstIds.Add(alert.PolicyId);
                }
            }
        }


        internal override void FlushMessages()
        {
            foreach (var (sendTime, branch) in _storage)
                if (sendTime < DateTime.UtcNow && _storage.TryRemove(sendTime, out _))
                {
                    foreach (var (_, message) in branch)
                    {
                        if (!message.IsSingleAlert || !_sendFirstIds.Contains(message.PolicyId))
                            SendAlertMessage(message);

                        _sendFirstIds.Remove(message.PolicyId);
                    }

                    branch.Clear();
                }
        }
    }
}