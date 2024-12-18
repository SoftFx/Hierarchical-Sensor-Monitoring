using System;
using System.Collections.Generic;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;

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
                try
                {
                    var sendFirstAlerts = new List<AlertResult>(1 << 2);
                    var sensorId = message.SensorId;

                    var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);

                    SendAlertMessage(sensorId, notApplyAlerts);

                    foreach (var alert in applyAlerts)
                    {

                        var grouppedAlerts = _storage.GetOrAdd(alert.SendTime);

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
                catch (Exception ex) 
                {
                    _logger.Error(ex);
                }
            }
        }


        internal override void FlushMessages()
        {
            lock (_lock)
            {
                try
                {
                    foreach (var (sendTime, branch) in _storage)
                        if (sendTime < DateTime.UtcNow && _storage.Remove(sendTime, out _))
                        {
                            foreach (var (_, message) in branch)
                                SendAlertMessage(message.FilterMessage());

                            branch.Clear();
                        }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }
    }
}