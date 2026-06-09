using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache
{
    internal sealed class DisabledStates
    {
        private readonly Dictionary<(Guid sensorId, string signature), bool> _entries = [];

        public void Set(Guid sensorId, string signature, bool isDisabled) =>
            _entries[(sensorId, signature)] = isDisabled;

        public bool? Get(Guid sensorId, string signature) =>
            _entries.TryGetValue((sensorId, signature), out var v) ? v : null;

        public static string GetSignature(Policy policy)
        {
            return string.Join("|", policy.Conditions
                .OrderBy(c => c.Property)
                .ThenBy(c => c.Operation)
                .Select(c => $"{c.Property}:{c.Operation}:{c.Target}"));
        }
    }
}
