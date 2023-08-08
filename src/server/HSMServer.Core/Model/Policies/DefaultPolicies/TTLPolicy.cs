using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using System;
using System.Text;

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

        public override string ToString()
        {
            var sb = new StringBuilder(1 << 5);

            sb.Append($"If Inactivity period = {_ttl.CurValue}");

            if (!string.IsNullOrEmpty(Icon))
                sb.Append($", then icon={Icon}");

            if (!string.IsNullOrEmpty(Template))
                sb.Append($", then template={Template}");

            if (!Status.IsOk())
                sb.Append($", change status to = {Status}");

            if (IsDisabled)
                sb.Append(" (disabled)");

            return sb.ToString();
        }
    }
}