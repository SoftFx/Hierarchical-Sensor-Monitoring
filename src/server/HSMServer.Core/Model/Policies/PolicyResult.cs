using HSMServer.Core.Model.Policies;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult : IEnumerable<AlertResult>
    {
        internal static PolicyResult Ok { get; } = new();


        public Dictionary<(string, string), AlertResult> Alerts { get; }

        public Guid SensorId { get; }


        public bool IsOk => Alerts.Count == 0;


        public PolicyResult()
        {
            Alerts = new();
        }

        internal PolicyResult(Guid sensorId) : this()
        {
            SensorId = sensorId;
        }

        internal PolicyResult(Guid sensorId, Policy policy) : this(sensorId)
        {
            AddAlert(policy);
        }


        internal void AddAlert(Policy policy)
        {
            var key = policy.AlertKey;

            if (Alerts.TryGetValue(key, out var alert))
                alert.AddComment(policy.AlertComment);
            else
                Alerts.Add(key, new AlertResult(policy));
        }


        public IEnumerator<AlertResult> GetEnumerator() => Alerts.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}