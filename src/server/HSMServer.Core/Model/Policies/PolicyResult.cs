using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public readonly struct PolicyResult
    {
        private readonly Dictionary<(string icon, string template), (int count, string lastComment)> _alerts;


        internal static PolicyResult Ok { get; } = new();


        public Guid SensorId { get; }

        public bool IsOk => _alerts.Count == 0;


        public PolicyResult()
        {
            _alerts = new();
        }

        internal PolicyResult(Guid sensorId) : this()
        {
            SensorId = sensorId;
        }

        internal PolicyResult(Guid sensorId, DataPolicy policy) : this(sensorId)
        {
            AddAlert(policy);
        }


        internal void AddAlert(DataPolicy policy)
        {
            var key = policy.AlertKey;
            var comment = policy.AlertComment;

            if (_alerts.TryGetValue(key, out var alert))
                _alerts[key] = (alert.count + 1, comment);
            else
                _alerts.Add(key, (1, comment));
        }
    }
}
