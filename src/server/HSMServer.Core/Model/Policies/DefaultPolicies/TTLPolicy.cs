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

        // LOAD-BEARING initializer (#1405): DateTime.MinValue, NOT null. A
        // null clock is a sentinel produced only by CancelNotification and
        // the resolution arm — "the alert was live and its send state was
        // reset" — and the never-sent bypass in ShouldResend delivers at
        // once on it. MinValue means "loaded from storage / never sent",
        // which the repeat-CADENCE gate must own instead. If this
        // initializer ever becomes null, every scheduled policy on a stale
        // sensor fires from the resend loop on the FIRST sweep after boot,
        // bypassing the repeat-mode gate (Immediately included).
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

        internal bool HasTimeout(DateTime? time) => IsActive && IsStale(time);

        // Staleness is read INDEPENDENTLY of enablement (#1404): "time since
        // the last value exceeds the interval" has an answer even for a
        // disabled policy, and the per-policy recovery gate in
        // SetExpiredSnapshot needs that answer for every policy in the
        // snapshot — a disable on an expired sensor must not flip a
        // window-caused resolution into a false recovery Ok. A TTL-from-parent
        // policy resolves _ttl.Value through the parent chain: with a parent
        // interval present it READS THE PARENT'S VALUE and can be stale;
        // "TTL-less reads as not stale" holds only when no interval resolves
        // anywhere (the resolved model is None — nothing to exceed).
        internal bool IsStale(DateTime? time) => !_ttl.IsEmpty && time.HasValue && _ttl.Value.TimeIsUp(time.Value);

        // The IN-WINDOW remainder of the resend decision, PURE — no writes:
        // the sweep (RunSensorTimeoutStep) owns the state machine and
        // performs the out-of-window cancellation itself, so no Try*-shaped
        // predicate hides an operator-visible clock reset from a second
        // caller. Precondition: the caller has already established
        // HasTimeout(time) and in-window — the window arm answering BEFORE
        // this method is what keeps a resolved-then-out-of-window policy
        // from delivering on every sweep tick (the zombie, #1405).
        //
        // The evaluation instant arrives from the caller (the sweep captures
        // one per pass) so the repeat-interval comparison below reads the
        // SAME timestamp as the window decision, not a fresh UtcNow (#1404).
        // The unsynchronized state reads are the tolerated trade-off (worst
        // case one duplicate notification); details:
        // aicontext/features/server/alerts/feature.md (#1405).
        internal bool ShouldResend(DateTime evaluationTime)
        {
            // Never-sent bypass, SCHEDULED policies only (#1405): the null
            // clock is produced only by the sweep's out-of-window
            // cancellation or a resolution — the fresh delivery at window
            // open must outrank the repeat-mode gate, or the DEFAULT mode
            // (Immediately, whose Schedule.IsActive is false) loses the
            // alert forever on the mixed-sensor shape. Schedule-less
            // policies keep the master order (the repeat-mode gate first):
            // their null clock pairs with a resolved sensor, and the
            // re-expiry transition re-arms it before this loop runs.
            if (ScheduleId.HasValue && !_lastTTLNotificationTime.HasValue)
                return true;

            if (!Schedule.IsActive)
                return false;

            if (!_lastTTLNotificationTime.HasValue)
                return true;

            return evaluationTime - _lastTTLNotificationTime >= Schedule.GetShiftTime();
        }

        // The shared out-of-window decision of the two gates (#1404 expiry,
        // #1405 repeat cancellation): one home for the fail-open semantics
        // (a null ScheduleId reads as in-window). Callers pass their
        // evaluation instant explicitly, so a decision and the gates keyed
        // on it cannot disagree across a minute/window boundary (#1404).
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
