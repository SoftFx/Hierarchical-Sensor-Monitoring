using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using System;
using System.Linq;
using System.Text;

namespace HSMServer.Core.Model.Policies
{
    public sealed class TTLPolicy : DefaultPolicyBase
    {
        public const byte Key = 255;
        public const string DefaultIcon = "🕑";
        public const string DefaultTemplate = "[$product]$path";

        private readonly TimeIntervalSettingProperty _ttl = new();
        private readonly OkPolicy _okPolicy;

        private DateTime? _lastTTLNotificationTime = DateTime.MinValue;

        private bool IsActive => !_ttl.IsEmpty && !IsDisabled;

        private int _notifyCount;

        internal int RetryCount => _notifyCount - 1;

        internal long? TTLTicks => _ttl.IsEmpty ? null : _ttl.Value?.Ticks;

        public TimeIntervalModel TTLInterval => _ttl.CurValue;


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
            if (entity?.TTL.HasValue == true)
                _ttl.TrySetValue(new TimeIntervalModel(entity.TTL.Value));
            else if (node?.Settings?.TTL?.Value != null)
                _ttl.TrySetValue(node.Settings.TTL.Value);

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
                Destination = new PolicyDestinationEntity() { UseDefaultChats = true},
            }, node as BaseSensorModel);

            _okPolicy = new OkPolicy(this, node);
        }

        internal TTLPolicy(TimeIntervalSettingProperty interval, PolicyEntity entity)
        {
            if (interval?.Value != null)
                _ttl.TrySetValue(interval.Value);

            Apply(entity ?? new PolicyEntity
            {
                Id = Id.ToByteArray(),
                Template = DefaultTemplate,
                Icon = DefaultIcon,
                Destination = new PolicyDestinationEntity() { UseDefaultChats = true },
            }, null);

            _okPolicy = new OkPolicy(this, null);
        }

        public override PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),
            Conditions = Conditions?.Select(u => u.ToEntity()).ToList(),
            Destination = Destination.ToEntity(),
            Schedule = Schedule.ToEntity(),
            ConfirmationPeriod = ConfirmationPeriod,
            SensorStatus = (byte)Status,
            IsDisabled = IsDisabled,
            Template = Template,
            Icon = Icon,
            TemplateId = TemplateId.HasValue ? TemplateId.Value.ToByteArray() : [],
            ScheduleId = ScheduleId.HasValue ? ScheduleId.Value.ToByteArray() : [],
            TTL = TTLTicks,
        };

        internal void ApplyParent(TTLPolicy parent, bool disable = false)
        {
            var update = new PolicyUpdate()
            {
                Destination = new PolicyDestinationUpdate(parent.Destination),
                Id = Id,
                Template = parent.Template,
                Icon = parent.Icon,
                IsDisabled = disable,
                TTL = parent.TTLTicks,
            };

            FullUpdate(update, Sensor);
        }

        public void FullUpdate(PolicyUpdate update, BaseSensorModel sensor = null)
        {
            if (update.TTL.HasValue)
                _ttl.TrySetValue(new TimeIntervalModel(update.TTL.Value));

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

        internal void InitLastTtlTime(DateTime time)
        {
            _lastTTLNotificationTime = time;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"If Inactivity period = {_ttl.CurValue}");

            return ActionsToString(sb).ToString();
        }
    }
}
