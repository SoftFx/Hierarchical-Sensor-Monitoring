using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using System;
using System.Text;
using System.Xml.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        public const byte Key = 255;
        public const string DefaultIcon = "🕑";
        public const string DefaultTemplate = "[$product]$path";

        private readonly SettingPropertyBase<TimeIntervalModel> _ttl;
        private readonly OkPolicy _okPolicy;

        private DateTime? _lastTTLNotificationTime = DateTime.MinValue;

        private bool IsActive => !_ttl.IsEmpty && !IsDisabled;

        private int _notifyCount;

        internal int RetryCount => _notifyCount - 1;


        internal PolicyResult Ok
        {
            get
            {
                _okPolicy.RebuildState();

                return _okPolicy.PolicyResult;
            }
        }

        public TTLPolicy()
        {
            _okPolicy = new OkPolicy(this, null); 
        }

        internal TTLPolicy(BaseNodeModel node, PolicyEntity entity)
        {
            _ttl = node.Settings.TTL;

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
                Destination = new PolicyDestinationEntity() { UseDefaultChats = true},
            }, node as BaseSensorModel);

            _okPolicy = new OkPolicy(this, node);
        }


        internal void ApplyParent(TTLPolicy parent, bool disable = false)
        {
            var update = new PolicyUpdate()
            {
                Destination = new PolicyDestinationUpdate(parent.Destination),
                Id = Id,
                Template = parent.Template,
                Icon = parent.Icon,
                IsDisabled = disable,
            };

            FullUpdate(update, Sensor);
        }

        public void FullUpdate(PolicyUpdate update, BaseSensorModel sensor = null)
        {
            TryUpdate(update, out _, sensor);

            _okPolicy.TryUpdate(update with { Template = _okPolicy.OkTemplate, Icon = null }, out _, sensor);
        }

        internal bool HasTimeout(DateTime? time) => IsActive && time.HasValue && _ttl.Value.TimeIsUp(time.Value);

        internal bool ResendNotification(DateTime? time)
        {
            if (!HasTimeout(time))
                return false;

            if(!Schedule.IsActive)
                return false;

            if (!_lastTTLNotificationTime.HasValue)
                return true;

            return DateTime.UtcNow - _lastTTLNotificationTime >= Schedule.GetShiftTime();

             //DateTime.UtcNow >= _lastTTLNotificationTime?.Add(Schedule.GetShiftTime());
        }

        internal PolicyResult GetNotification(bool timeout)
        {
            if (timeout)
            {
                _lastTTLNotificationTime = DateTime.UtcNow;
                _notifyCount++;

                return PolicyResult;
            }

            _lastTTLNotificationTime =  null;
            _notifyCount = 0;

            return Ok;
        }

        internal void InitLastTtlTime(bool timeout)
        {
            _lastTTLNotificationTime = timeout ? DateTime.UtcNow : null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"If Inactivity period = {_ttl.CurValue}");

            return ActionsToString(sb).ToString();
        }
    }
}