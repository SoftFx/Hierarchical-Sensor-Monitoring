using HSMServer.Core.Model.NodeSettings;
using System;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        private readonly SettingProperty<TimeIntervalModel> _ttl;


        internal TTLPolicy(Guid sensorId, SettingProperty<TimeIntervalModel> ttlSetting) : base(sensorId)
        {
            _ttl = ttlSetting;

            Icon = "⌛️";
        }


        internal bool HasTimeout(DateTime? time) => !_ttl.IsEmpty && time.HasValue && _ttl.Value.TimeIsUp(time.Value);
    }
}