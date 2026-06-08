using System;
using System.Collections.Generic;

namespace HSMServer.Core.Cache
{
    internal sealed class DisabledStates
    {
        private readonly Dictionary<(Guid sensorId, int index), bool> _policies = [];
        private readonly Dictionary<(Guid sensorId, int index), bool> _ttls = [];

        public void SetPolicy(Guid sensorId, int index, bool isDisabled) =>
            _policies[(sensorId, index)] = isDisabled;

        public void SetTtl(Guid sensorId, int index, bool isDisabled) =>
            _ttls[(sensorId, index)] = isDisabled;

        public bool? GetPolicy(Guid sensorId, int index) =>
            _policies.TryGetValue((sensorId, index), out var v) ? v : null;

        public bool? GetTtl(Guid sensorId, int index) =>
            _ttls.TryGetValue((sensorId, index), out var v) ? v : null;
    }
}
