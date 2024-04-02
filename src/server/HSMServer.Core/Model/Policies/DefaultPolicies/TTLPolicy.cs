using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
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
        private readonly OkPolicy _okPolicy;

        private bool IsActive => !_ttl.IsEmpty && !IsDisabled;


        internal PolicyResult Ok
        {
            get
            {
                _okPolicy.RebuildState();

                return _okPolicy.PolicyResult;
            }
        }


        internal TTLPolicy(BaseNodeModel node, PolicyEntity entity)
        {
            _ttl = node.Settings.TTL;

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
                Destination = new PolicyDestinationEntity(),
            }, node as BaseSensorModel);

            _okPolicy = new OkPolicy(this, node);
        }


        internal void ApplyParent(TTLPolicy parent, bool disable = false)
        {
            var update = new PolicyUpdate()
            {
                Destination = new PolicyDestinationUpdate(parent.Destination.Chats, parent.Destination.AllChats),
                Id = Id,
                Template = parent.Template,
                Icon = parent.Icon,
                IsDisabled = disable,
            };

            FullUpdate(update, Sensor);
        }

        internal void FullUpdate(PolicyUpdate update, BaseSensorModel sensor = null)
        {
            TryUpdate(update, out _, sensor);

            _okPolicy.TryUpdate(update with { Template = _okPolicy.OkTemplate, Icon = null }, out _, sensor);
        }


        internal bool HasTimeout(DateTime? time) => IsActive && time.HasValue && _ttl.Value.TimeIsUp(time.Value);

        internal bool ResendNotification(DateTime? time) => Schedule.IsActive && HasTimeout(time) &&
            DateTime.UtcNow >= time.Value.Add(Schedule.GetShiftTime());

        internal PolicyResult GetNotification(bool timeout) => timeout ? PolicyResult : Ok;


        public override string ToString()
        {
            var sb = new StringBuilder($"If Inactivity period = {_ttl.CurValue}");

            return ActionsToString(sb).ToString();
        }
    }
}