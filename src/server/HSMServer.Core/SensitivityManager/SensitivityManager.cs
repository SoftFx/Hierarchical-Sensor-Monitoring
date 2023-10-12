using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Sensitivity
{
    internal sealed class SensitivityStorage
    {
        private readonly CGuidDict<CGuidDict<CPriorityQueue<AlertResult, DateTime>>> _tree = new(); //sensorId -> alertId -> alertResult
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
                    if (!newAlerts.ContainsKey(storedAlertId))
                        branch.TryRemove(storedAlertId, out _);

                foreach (var alertId in newAlerts.Keys.ToList())
                {
                    var alert = newAlerts[alertId];

                    if (alert.Sensativity is not null)
                    {
                        branch[alertId].Enqueue(alert, DateTime.UtcNow);
                        newAlerts.Remove(alertId);
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

                    foreach (var (alertId, alertResults) in sensorAlerts)
                    {
                        while (alertResults.TryPeek(out var alertResult, out var stateTime))
                        {
                            if ((DateTime.UtcNow - stateTime).Ticks > alertResult.Sensativity.Ticks)
                            {
                                alertResults.TryDequeue(out _, out _);
                                thrownAlerts.Add(alertResult);
                            }
                            else
                                break;
                        }

                        if (alertResults.IsEmpty)
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