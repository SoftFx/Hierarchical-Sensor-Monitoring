using HSMServer.Core.Model.NodeSettings;
using System;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        private const string DefaultIcon = "🕑";
        private const string DefaultTemplate = "[$product]$path";


        private readonly SettingProperty<TimeIntervalModel> _ttl;


        public override string Icon { get; protected set; } = DefaultIcon;

        public override string Template { get; protected set; } = DefaultTemplate;


        internal TTLPolicy(Guid nodeId, SettingProperty<TimeIntervalModel> ttlSetting) : base(nodeId)
        {
            _ttl = ttlSetting;
        }


        internal bool HasTimeout(DateTime? time) => !_ttl.IsEmpty && time.HasValue && _ttl.Value.TimeIsUp(time.Value);
    }
}