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


        public bool ShouldSendFirstMessage(AlertResult alert)
        {
            var messagesCnt = _alerts.TryGetValue(alert.PolicyId, out var alerts) ? alerts.Count : 0;

            return alert.ShouldSendFirstMessage && messagesCnt == 0;
        }

        public void AddAlert(AlertResult result)
        {
            var key = result.PolicyId;

            if (result.IsReplaceAlert)
                RemovePolicyAlerts(key);

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

        public AlertMessage FilterMessage()
        {
            foreach (var (policyId, messages) in _alerts)
                if (messages[0].ShouldSendFirstMessage && messages.Count == 1)
                {
                    _totalAlerts -= messages.Count;
                    _alerts.Remove(policyId);
                }

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
        public ScheduleAlertMessage() : base(Guid.Empty) { }

        internal ScheduleAlertMessage(Guid sensorId) : base(sensorId) { }
    }
}