using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Model
{
    public sealed class PolicyResult : IEnumerable<AlertResult>
    {
        internal static PolicyResult Ok => new();

        public Dictionary<Guid, AlertResult> Alerts { get; }

        public bool IsEmpty => Alerts.Count == 0;



        public PolicyResult()
        {
            Alerts = [];
        }


        internal PolicyResult(Policy policy) : this()
        {
            AddAlert(policy);
        }


        internal void AddSingleAlert(Policy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);

            Alerts[policy.Id] = new AlertResult(policy, true);
        }

        internal void AddAlert(Policy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);

            var key = policy.Id;

            if (Alerts.TryGetValue(key, out var alert))
                alert.AddPolicyResult(policy);
            else
                Alerts.Add(key, new AlertResult(policy));
        }

        internal PolicyResult LeftOnlyScheduled()
        {
            var keysToRemove = Alerts
                .Where(pair => !pair.Value.IsScheduleAlert)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var id in keysToRemove)
                Alerts.Remove(id);

            return this;
        }


        internal void RemoveAlert(Policy policy) => Alerts.Remove(policy.Id);

        internal void RemoveAlert(AlertResult alert) => Alerts.Remove(alert.PolicyId);


        public IEnumerator<AlertResult> GetEnumerator() => Alerts.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}