using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Confirmation
{
    internal sealed class ConfirmationManager : BaseTimeManager
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, PriorityQueue<AlertResult, DateTime>>> _tree = new();

        internal void RegisterNotification(Guid sensorId, PolicyResult policyResult)
        {
            _logger.Info($"RegisterNotification enter: sensor:{sensorId}, is empty: {policyResult.IsEmpty}");

            if (policyResult.IsEmpty)
                return;

            var alertsToSend = new List<AlertResult>();
            var currentTime = DateTime.UtcNow;

            try
            {
                var branch = _tree.GetOrAdd(sensorId, _ => new ConcurrentDictionary<Guid, PriorityQueue<AlertResult, DateTime>>());
                var newAlerts = new Dictionary<Guid, AlertResult>(policyResult.Alerts);

                RemoveInvalidAlerts(branch, newAlerts);

                foreach (var (alertId, alert) in newAlerts)
                {
                    if (!alert.IsValidAlert) continue;

                    if (alert.ConfirmationPeriod is not null)
                    {
                        var queue = branch.GetOrAdd(alertId, _ => new PriorityQueue<AlertResult, DateTime>());
                        queue.Enqueue(alert, currentTime);
                    }
                    else
                    {
                        alertsToSend.Add(alert);
                    }
                }

                if (alertsToSend.Count > 0)
                {
                    _logger.Info($"Sending {alertsToSend.Count} immediate alerts for {sensorId}");
                    SendAlertMessage(sensorId, alertsToSend);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private static void RemoveInvalidAlerts(ConcurrentDictionary<Guid, PriorityQueue<AlertResult, DateTime>> branch, Dictionary<Guid, AlertResult> newAlerts)
        {
            if (branch == null || newAlerts == null) return;

            var idsToRemove = branch.Keys.Except(newAlerts.Keys).ToList();
            foreach (var id in idsToRemove)
                branch.TryRemove(id, out _);

        }

        internal void UpdateNotifications(Guid sensorId, PolicyResult policyResult)
        {
            try
            {
                if (_tree.TryGetValue(sensorId, out var branch))
                    foreach (var alertResult in policyResult)
                        branch.TryRemove(alertResult.PolicyId, out _);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        internal override void FlushMessages()
        {
            try
            {
                var messagesToSend = new Dictionary<Guid, List<AlertResult>>();
                var currentTime = DateTime.UtcNow;

                foreach (var (sensorId, sensorAlerts) in _tree)
                {
                    var thrownAlerts = new List<AlertResult>();

                    foreach (var (alertId, allResults) in sensorAlerts)
                    {
                        while (allResults.TryPeek(out var result, out var stateTime))
                        {
                            if ((currentTime - stateTime).Ticks > result.ConfirmationPeriod.Value)
                            {
                                allResults.TryDequeue(out var dequeuedResult, out _);

                                if (!result.IsStatusIsChangeResult)
                                {
                                    thrownAlerts.Add(dequeuedResult);
                                }
                                else
                                {
                                    var statusChanges = ProcessStatusChangeAlerts(allResults, dequeuedResult);
                                    if (statusChanges.Count > 0)
                                        thrownAlerts.AddRange(statusChanges);
                                }
                            }
                            else break;
                        }

                        if (allResults.Count == 0)
                            sensorAlerts.TryRemove(alertId, out _);
                    }

                    if (thrownAlerts.Count > 0)
                        messagesToSend[sensorId] = thrownAlerts;
                }

                foreach(var message in messagesToSend)
                    SendAlertMessage(message.Key, message.Value);

                CleanEmptyBranches();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private static List<AlertResult> ProcessStatusChangeAlerts(PriorityQueue<AlertResult, DateTime> queue, AlertResult firstAlert)
        {
            var statusChanges = new List<AlertResult> { firstAlert };

            while (queue.TryDequeue(out var nextResult, out _))
                statusChanges.Add(nextResult);

            return statusChanges[0].LastState.PrevStatus != statusChanges[^1].LastState.Status
                ? statusChanges
                : [];
        }

        private void CleanEmptyBranches()
        {
            foreach (var (sensorId, branch) in _tree)
                if (branch.IsEmpty)
                    _tree.TryRemove(sensorId, out _);
        }
    }
}