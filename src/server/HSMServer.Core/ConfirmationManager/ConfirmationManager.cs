using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Confirmation
{
    internal sealed class ConfirmationManager
    {
        private readonly CGuidDict<CGuidDict<CPriorityQueue<AlertResult, DateTime>>> _tree = new(); //sensorId -> alertId -> alertResult
        private readonly ConcurrentDictionary<Guid, AlertResult> _lastStatusUpdates = new();

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public event Action<Guid, List<AlertResult>> ThrowAlertResultsEvent;


        internal void SaveOrSendPolicies(PolicyResult policyResult)
        {
            try
            {
                var newAlerts = new Dictionary<Guid, AlertResult>(policyResult.Alerts);
                var sensorId = policyResult.SensorId;
                var branch = _tree[sensorId];

                foreach (var (storedAlertId, _) in branch)
                    if (!newAlerts.ContainsKey(storedAlertId) && !_lastStatusUpdates.ContainsKey(storedAlertId))
                        branch.TryRemove(storedAlertId, out _);

                foreach (var alertId in newAlerts.Keys.ToList())
                {
                    var alert = newAlerts[alertId];

                    if (alert.ConfirmationPeriod is not null)
                    {
                        branch[alertId].Enqueue(alert, DateTime.UtcNow);
                        newAlerts.Remove(alertId);

                        if (alert.IsStatusIsChangeResult)
                            _lastStatusUpdates.AddOrUpdate(alertId, alert, (_, _) => alert);
                    }
                }

                if (newAlerts.Count > 0)
                    ThrowAlertResultsEvent?.Invoke(sensorId, newAlerts.Values.ToList());
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }


        internal void FlushStorage()
        {
            try
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
                                    if (allResults.TryPeekValue(out var first) && _lastStatusUpdates.TryGetValue(alertId, out var last) && first.LastState.PrevStatus != last.LastState.Status)
                                        thrownAlerts.AddRange(allResults.UnwrapToList());

                                    _lastStatusUpdates.TryRemove(alertId, out _);
                                    allResults.Clear();
                                }
                            }
                            else
                                break;
                        }

                        if (allResults.IsEmpty)
                            sensorAlerts.TryRemove(alertId, out _);
                    }

                    if (sensorAlerts.IsEmpty)
                        _tree.TryRemove(sensorId, out _);

                    if (thrownAlerts.Count > 0)
                        ThrowAlertResultsEvent?.Invoke(sensorId, thrownAlerts);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }
    }
}