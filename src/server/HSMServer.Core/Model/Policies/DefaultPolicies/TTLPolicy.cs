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
        // The schedule gates differ ON PURPOSE between the two policy kinds:
        // the regular data-policy gate in SensorPolicyCollection keeps
        // VALUE-TIME semantics (the value IS the event there — an old value
        // arriving out-of-window must be judged by its own timestamp), while
        // the TTL gates (#1404 expiry, #1405 repeat) use EVALUATION time — a
        // stale in-session value must not fire when "now" is outside the
        // window. Do not "unify" them: either choice re-breaks #1404.
        //
        // Tolerated race: this method reads _lastTTLNotificationTime and, via
        // CancelNotification, writes it back with no lock — the state pair
        // (_lastTTLNotificationTime, _notifyCount) is already written from
        // two threads (the sweep via SetExpiredSnapshot, the API thread via
        // TryAddValue -> SensorTimeout). Worst case a value arriving while the
        // sweep is inside this method costs ONE duplicate notification; the
        // unsynchronized read-modify-write is the established trade-off here
        // (cf. the #1296 notes in BaseSensorModelT), not an oversight.
        internal bool ResendNotification(DateTime? time, IAlertScheduleProvider scheduleProvider)
        {
            if (!HasTimeout(time))
                return false;

            // Outside the alert's schedule window the repeat is CANCELLED,
            // not paused (#1405): the notification state resets, so a window
            // open with a still-stale value fires a FRESH alert (the null
            // timestamp makes the next in-window evaluation send at once)
            // instead of resuming yesterday's cadence. A scheduled policy
            // goes silent in a mixed sensor while its schedule-less siblings
            // keep their own rules — the cancellation is per-policy because
            // the schedule is per-alert (OffTime, by contrast, is
            // sensor-wide and must never be fabricated here). MANDATORY with
            // the #1404 evaluation-time expiry gate: that gate resolves the
            // sensor out-of-window, GetNotification(false) nulls the
            // timestamp, and the `!HasValue` arm below would otherwise send
            // on EVERY sweep tick outside the window. The arm also answers
            // FIRST, before the repeat-mode gate: it is the only home of the
            // out-of-window reset, and every repeat mode must pass through
            // it on the way to the never-sent arm below.
            if (IsOutsideSchedule(scheduleProvider))
            {
                CancelNotification();
                return false;
            }

            // A null timestamp means "not delivered in this window": the
            // out-of-window transition cancelled the alert (a schedule-less
            // sibling flipped a mixed sensor while this policy's window was
            // shut), or a resolve reset it. That delivery must happen at the
            // first in-window sweep — for EVERY repeat mode, hence BEFORE
            // the IsActive gate: Immediately (the DEFAULT mode) leaves
            // Schedule.IsActive false, and on the mixed-sensor shape there
            // is no second sensor transition at window open (the sensor is
            // already expired), so this arm is the only delivery path left —
            // gating it on the repeat mode lost the alert forever while the
            // UI kept showing it active (PR #1406 round 2, finding 1).
            if (!_lastTTLNotificationTime.HasValue)
                return true;

            // The repeat cadence governs only alerts already delivered in
            // this window; Immediately never repeats by design.
            if (!Schedule.IsActive)
                return false;

            return DateTime.UtcNow - _lastTTLNotificationTime >= Schedule.GetShiftTime();
        }

        // The shared out-of-window decision of the two gates (#1404 expiry,
        // #1405 repeat cancellation): one home for the fail-open semantics
        // (a null ScheduleId reads as in-window).
        internal bool IsOutsideSchedule(IAlertScheduleProvider scheduleProvider) =>
            ScheduleId.HasValue && !scheduleProvider.IsWorkingTime(ScheduleId.Value, DateTime.UtcNow);

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
