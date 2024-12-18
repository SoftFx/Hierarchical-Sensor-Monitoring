using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Confirmation
{
    internal sealed class ConfirmationManager : BaseTimeManager
    {
        private readonly Dictionary<Guid, Dictionary<Guid, PriorityQueue<AlertResult, DateTime>>> _tree = new(); //sensorId -> alertId -> alertResult
        private readonly Dictionary<Guid, AlertResult> _lastStatusUpdates = new();

        private readonly object _lock = new object();

        internal void RegisterNotification(PolicyResult policyResult)
        {
            try
            {
                lock (_lock)
                {
                    var newAlerts = new Dictionary<Guid, AlertResult>(policyResult.Alerts);
                    var sensorId = policyResult.SensorId;
                    var branch = _tree.GetOrAdd(sensorId);

                    FlushNotValidAlerts(branch, newAlerts);

                    if (policyResult.IsEmpty)
                        return;

                    foreach (var alertId in newAlerts.Keys.ToList())
                    {
                        var alert = newAlerts[alertId];

                        if (!alert.IsValidAlert)
                            newAlerts.Remove(alertId);
                        else if (alert.ConfirmationPeriod is not null)
                        {
                            branch.GetOrAdd(alertId).Enqueue(alert, DateTime.UtcNow);
                            newAlerts.Remove(alertId);

                            if (alert.IsStatusIsChangeResult)
                                _lastStatusUpdates.Add(alertId, alert);
                        }
                    }

                    SendAlertMessage(sensorId, [.. newAlerts.Values]);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private void FlushNotValidAlerts(Dictionary<Guid, PriorityQueue<AlertResult,DateTime>> branch, Dictionary<Guid, AlertResult> newAlerts)
        {
            foreach (var (storedAlertId, _) in branch)
                if (!newAlerts.ContainsKey(storedAlertId) && !_lastStatusUpdates.ContainsKey(storedAlertId))
                    branch.Remove(storedAlertId, out _);
        }
        
        internal override void FlushMessages()
        {
            try
            {
                lock (_lock)
                {
                    foreach (var (sensorId, sensorAlerts) in _tree)
                    {
                        var thrownAlerts = new List<AlertResult>(1 << 4);

                        foreach (var (alertId, allResults) in sensorAlerts)
                        {
                            while (allResults.TryPeek(out var result, out var stateTime))
                            {
                                if ((DateTime.UtcNow - stateTime).Ticks > result.ConfirmationPeriod.Value)
                                {
                                    if (!result.IsStatusIsChangeResult)
                                    {
                                        allResults.TryDequeue(out _, out _);
                                        thrownAlerts.Add(result);
                                    }
                                    else
                                    {
                                        if (allResults.TryPeek(out var first, out _) &&
                                            _lastStatusUpdates.TryGetValue(alertId, out var last) &&
                                            first.LastState.PrevStatus != last.LastState.Status)
                                            thrownAlerts.AddRange(allResults.UnorderedItems.Select(x => x.Element));

                                        _lastStatusUpdates.Remove(alertId, out _);
                                        allResults.Clear();
                                    }
                                }
                                else
                                    break;
                            }

                            if (allResults.Count > 0)
                                sensorAlerts.Remove(alertId, out _);
                        }

                        if (sensorAlerts.Count == 0)
                            _tree.Remove(sensorId, out _);

                        SendAlertMessage(sensorId, thrownAlerts);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }
    }
}