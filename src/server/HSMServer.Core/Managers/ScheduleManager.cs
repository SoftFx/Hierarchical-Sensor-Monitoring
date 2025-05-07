using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Managers
{
    internal sealed class ScheduleManager : BaseTimeManager
    {
        private readonly ConcurrentDictionary<DateTime, ConcurrentDictionary<Guid, ScheduleAlertMessage>> _storage = new();
        private readonly object _flushLock = new object();

        internal void ProcessMessage(AlertMessage message)
        {
            _logger.Info("ProcessMessage started");

            var (notApplyAlerts, applyAlerts) = message.SplitByCondition(u => u.IsScheduleAlert);
            var sendFirstAlerts = new List<AlertResult>();
            var sensorId = message.SensorId;

            if (notApplyAlerts.Count > 0)
            {
                _logger.Info($"Sending {notApplyAlerts.Count} immediate alerts");
                SendAlertMessage(sensorId, notApplyAlerts);
            }

            foreach (var alert in applyAlerts)
            {
                try
                {
                    var timeGroup = _storage.GetOrAdd(alert.SendTime,
                        _ => new ConcurrentDictionary<Guid, ScheduleAlertMessage>());

                    var sensorGroup = timeGroup.GetOrAdd(sensorId,
                        id => new ScheduleAlertMessage(id));

                    if (sensorGroup.ShouldSendFirstMessage(alert))
                        sendFirstAlerts.Add(alert);

                    sensorGroup.AddAlert(alert);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to process alert for {sensorId}", ex);
                }
            }

            if (sendFirstAlerts.Count > 0)
            {
                _logger.Info($"Sending {sendFirstAlerts.Count} first alerts");
                SendAlertMessage(sensorId, sendFirstAlerts);
            }
        }

        internal override void FlushMessages()
        {
            if (_storage.IsEmpty)
                return;

            try
            {
                var currentTime = DateTime.UtcNow;
                var messagesToSend = new List<AlertMessage>();

                lock (_flushLock)
                {

                    foreach (var (sendTime, timeGroup) in _storage)
                    {
                        if (sendTime < currentTime && _storage.TryRemove(sendTime, out _))
                        {
                            foreach (var (sensorId, message) in timeGroup)
                            {
                                var filtered = message.FilterMessage();
                                if (!filtered.IsEmpty)
                                    messagesToSend.Add(filtered);
                            }
                        }
                    }
                }

                Parallel.ForEach(messagesToSend,
                    new ParallelOptions { MaxDegreeOfParallelism = 4 },
                    SendAlertMessage);
            }
            catch (Exception ex)
            {
                _logger.Error("Flush failed", ex);
            }
        }
    }
}