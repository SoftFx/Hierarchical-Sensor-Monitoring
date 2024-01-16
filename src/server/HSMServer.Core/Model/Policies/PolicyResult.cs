using HSMServer.Core.Model.Policies;
using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult : IEnumerable<AlertResult>
    {
        internal static PolicyResult Ok => new();


        public Dictionary<Guid, AlertResult> Alerts { get; }

        public Guid SensorId { get; }


        public bool IsEmpty => Alerts.Count == 0;


        public PolicyResult()
        {
            //Alerts = [];
            Alerts = new Dictionary<Guid, AlertResult>();
        }

        internal PolicyResult(Guid sensorId) : this()
        {
            SensorId = sensorId;
        }

        internal PolicyResult(Guid sensorId, Policy policy) : this(sensorId)
        {
            AddAlert(policy);
        }


        internal void AddSingleAlert(Policy policy)
        {
            var key = policy.Id;

            if (!Alerts.ContainsKey(key))
                Alerts.Add(key, new AlertResult(policy));
        }

        internal void AddAlert(Policy policy)
        {
            var key = policy.Id;

            if (Alerts.TryGetValue(key, out var alert))
                alert.AddPolicyResult(policy);
            else
                Alerts.Add(key, new AlertResult(policy));
        }


        internal void RemoveAlert(Policy policy) => Alerts.Remove(policy.Id, out var _);

        internal void RemoveAlert(AlertResult alert) => Alerts.Remove(alert.PolicyId, out var _);


        public IEnumerator<AlertResult> GetEnumerator() => Alerts.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}