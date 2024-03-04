using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    public class AlertMessage : IEnumerable<AlertResult>
    {
        private readonly Dictionary<Guid, List<AlertResult>> _alerts = [];
        private int _totalAlerts = 0;

        public Guid SensorId { get; }

        public Guid FolderId { get; private set; }


        public bool IsEmpty => _totalAlerts == 0;


        internal AlertMessage(Guid sensorId)
        {
            SensorId = sensorId;
        }

        internal AlertMessage(Guid sensorId, List<AlertResult> alerts) : this(sensorId)
        {
            alerts.ForEach(AddAlert);
        }


        public void AddAlert(AlertResult result)
        {
            var key = result.PolicyId;

            if (!_alerts.ContainsKey(key))
                _alerts.Add(key, []);

            _totalAlerts++;
            _alerts[key].Add(result);
        }

        public void RemovePolicyAlerts(Guid policyId)
        {
            if (_alerts.TryGetValue(policyId, out var alerts))
            {
                _totalAlerts -= alerts.Count;
                alerts.Clear();
            }
        }

        public AlertMessage ApplyFolder(ProductModel product)
        {
            FolderId = product.FolderId.Value;

            return this;
        }

        public IEnumerator<AlertResult> GetEnumerator()
        {
            foreach (var (_, policyAlerts) in _alerts)
                foreach (var alert in policyAlerts)
                    yield return alert;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }


    public sealed class ScheduleAlertMessage : AlertMessage
    {
        public Guid PolicyId { get; }


        public ScheduleAlertMessage() : base(Guid.Empty) { }

        internal ScheduleAlertMessage(Guid sensorId, Guid alertPolicyId) : base(sensorId)
        {
            PolicyId = alertPolicyId;
        }
    }
}