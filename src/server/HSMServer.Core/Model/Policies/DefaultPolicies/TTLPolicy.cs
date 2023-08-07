using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using System;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        public const string DefaultIcon = "🕑";
        public const string DefaultTemplate = "[$product]$path";

        private readonly SettingProperty<TimeIntervalModel> _ttl;


        internal OkPolicy Ok { get; }


        internal TTLPolicy(BaseNodeModel node, PolicyEntity entity)
        {
            _ttl = node.Settings.TTL;

            Ok = new OkPolicy(Id, node);

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
            }, node as BaseSensorModel);
        }


        internal bool HasTimeout(DateTime? time) => !_ttl.IsEmpty && time.HasValue && _ttl.Value.TimeIsUp(time.Value);
    }
}