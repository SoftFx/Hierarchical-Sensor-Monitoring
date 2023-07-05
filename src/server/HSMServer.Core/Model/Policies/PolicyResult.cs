﻿using HSMServer.Core.Model.Policies;
using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult : IEnumerable<AlertResult>
    {
        internal static PolicyResult Ok { get; } = new();


        public Dictionary<Guid, AlertResult> Alerts { get; }

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
            var key = policy.Id;

            if (Alerts.TryGetValue(key, out var alert))
                alert.AddPolicyResult(policy);
            else
                Alerts.Add(key, new AlertResult(policy));
        }


        public IEnumerator<AlertResult> GetEnumerator() => Alerts.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}