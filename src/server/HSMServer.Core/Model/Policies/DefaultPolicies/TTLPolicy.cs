using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Schedule;
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

        public TimeIntervalModel TTLInterval => _ttl.Value;

        public bool IsTTLFromParent => !_ttl.IsSet;

        internal void SetTTLParent(TimeIntervalSettingProperty parent) => _ttl.SetParent(parent);


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
            if (entity?.TTL is not null and not long.MaxValue)
                _ttl.TrySetValue(new TimeIntervalModel(entity.TTL.Value));
            // When entity.TTL is null, the policy stays in IsTTLFromParent state.
            // The owning PolicyCollectionBase wires the parent via SetTTLParent(...)
            // using its TTLParentSource (node-bounded for products, chain for sensors).

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
            TemplateAlertId = TemplateAlertId.HasValue ? TemplateAlertId.Value.ToByteArray() : [],
            TTL = IsTTLFromParent ? null : TTLTicks,
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
                TTL = parent.IsTTLFromParent || parent.TTLTicks == long.MaxValue ? null : parent.TTLTicks,
            };

            FullUpdate(update, Sensor);
        }

        public void FullUpdate(PolicyUpdate update, BaseSensorModel sensor = null)
        {
            if (update.TTL.HasValue && update.TTL.Value != long.MaxValue)
                _ttl.TrySetValue(new TimeIntervalModel(update.TTL.Value));
            else
                _ttl.TrySetValue(new TimeIntervalModel());

            TryUpdate(update, out _, sensor);

            _okPolicy.TryUpdate(update with { Template = _okPolicy.OkTemplate, Icon = null }, out _, sensor);
        }

        internal bool HasTimeout(DateTime? time) => IsActive && time.HasValue && _ttl.Value.TimeIsUp(time.Value);

        // The schedule provider arrives as a parameter (the policy owns no
        // provider — the collection and the cache do; the maintenance sweep
        // is the caller here and holds the shared instance). Fail-open
        // inherited from IsWorkingTime: an unknown schedule id reads as
        // in-window, the same fallback the expiry gate applies (#1405).
        //
        // The schedule gates deliberately differ between the two policy kinds
        // (TTL gates evaluate at UtcNow, data-policy gates at the value's
        // own timestamp) — the reasoning and the "do not unify" rule live in
        // aicontext/features/server/alerts/feature.md (#1404).
        //
        // Not side-effect-free: the out-of-window arm cancels the
        // notification state. The state pair (_lastTTLNotificationTime,
        // _notifyCount) is written from two threads without a lock — the
        // tolerated trade-off (worst case one duplicate notification);
        // details: aicontext/features/server/alerts/feature.md (#1405).
        internal bool TryResendNotification(DateTime? time, IAlertScheduleProvider scheduleProvider)
        {
            if (!HasTimeout(time))
                return false;

            // Outside the window the repeat is CANCELLED, not paused (#1405):
            // the state reset makes the next in-window evaluation deliver at
            // once (fresh), instead of resuming yesterday's cadence.
            if (IsOutsideSchedule(scheduleProvider))
            {
                CancelNotification();
                return false;
            }

            // Never-sent bypass, SCHEDULED policies only (#1405): the null
            // clock is produced only by the cancellation above or a
            // resolution — the fresh delivery at window open must outrank
            // the repeat-mode gate, or the DEFAULT mode (Immediately, whose
            // Schedule.IsActive is false) loses the alert forever on the
            // mixed-sensor shape. Schedule-less policies keep the master
            // order (the repeat-mode gate first): their null clock pairs
            // with a resolved sensor, and the re-expiry transition re-arms
            // it before this loop runs.
            if (ScheduleId.HasValue && !_lastTTLNotificationTime.HasValue)
                return true;

            if (!Schedule.IsActive)
                return false;

            if (!_lastTTLNotificationTime.HasValue)
                return true;

            return DateTime.UtcNow - _lastTTLNotificationTime >= Schedule.GetShiftTime();
        }

        // The shared out-of-window decision of the two gates (#1404 expiry,
        // #1405 repeat cancellation): one home for the fail-open semantics
        // (a null ScheduleId reads as in-window). Callers that already own an
        // evaluation instant pass it explicitly, so a decision and the gates
        // keyed on it cannot disagree across a minute/window boundary (#1404).
        internal bool IsOutsideSchedule(IAlertScheduleProvider scheduleProvider) =>
            IsOutsideSchedule(scheduleProvider, DateTime.UtcNow);

        internal bool IsOutsideSchedule(IAlertScheduleProvider scheduleProvider, DateTime evaluationTime) =>
            ScheduleId.HasValue && !scheduleProvider.IsWorkingTime(ScheduleId.Value, evaluationTime);

        // The per-policy half of GetNotification(false): resets the repeat
        // clock and the counter without going through the sensor-level
        // transition (which is what sends resolution notifications — a
        // per-policy window close must stay silent, #1405).
        internal void CancelNotification()
        {
            _lastTTLNotificationTime = null;
            _notifyCount = 0;
        }

        internal PolicyResult GetNotification(bool timeout)
        {
            if (timeout)
            {
                _lastTTLNotificationTime = DateTime.UtcNow;
                _notifyCount++;

                return PolicyResult;
            }

            CancelNotification();

            return Ok;
        }

        internal void InitLastTtlTime(DateTime time)
        {
            _lastTTLNotificationTime = time;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"If Inactivity Period = {_ttl.Value}");

            return ActionsToString(sb).ToString();
        }
    }
}
