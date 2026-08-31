using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Model.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Model
{

    public interface IBarSensor
    {
        BarBaseValue LocalLastValue { get; }
    }


    // Outcome of a bounded history-load retry (#1344). The sweep charges its per-sweep budget
    // on Failed alone: Suppressed did no database work, and Loaded is an ordinary read pair
    // that must not throttle recovery.
    internal enum HistoryLoadRetryResult
    {
        Suppressed,
        Failed,
        Loaded,
    }


    public abstract class BaseSensorModel : BaseNodeModel
    {
        private static readonly SensorResult _muteResult = new(SensorStatus.OffTime, "Muted");

        public override SensorSettingsCollection Settings { get; } = new();

        public override SensorPolicyCollection Policies { get; }

        public DateTime From => Storage.From;

        public DateTime To => Storage.To;

        protected abstract ValuesStorage Storage { get; }

        internal bool IsExpired { get; set; }

        public abstract SensorType Type { get; }

        public Dictionary<int, EnumOptionModel> EnumOptions { get; private set; } = new Dictionary<int, EnumOptionModel>();


        public bool IsSingleton { get; private set; }

        public bool AggregateValues { get; private set; }

        public StatisticsOptions Statistics { get; private set; }

        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public Unit? OriginalUnit { get; private set; }

        public RateDisplayUnit? DisplayUnit { get; protected set; }

        public SensorState State { get; private set; }

        public TableSettingsModel TableSettings { get; private set; } = new();

        public SensorResult? Status
        {
            get
            {
                if (State == SensorState.Muted)
                    return _muteResult;

                return !Policies.SensorResult.IsOk ? Policies.SensorResult : Storage.Result;
            }
        }

        public void Clear(DateTime to) => Storage.Clear(to);

        public PolicyResult Notifications => Policies.NotificationResult;

        public PolicyResult PolicyResult => Policies.PolicyResult;

        public PolicyResult ConfirmationResult => Policies.ConfimationResult;


        // False until Initialize() has published a SUCCESSFUL history load (#1296, #1328). Not a
        // retry gate: the internal latch stays latched on failure (until the bounded
        // RetryFailedHistoryLoad re-arm, #1344) — use for deferring decisions, not for deciding
        // whether to attempt a load.
        internal abstract bool IsHistoryLoaded { get; }

        // True when a load was attempted but failed: the latch stays latched on failure until
        // RetryFailedHistoryLoad reruns the load (bounded, #1344), so between retries
        // self-destroy is disabled for such sensors, not deferred.
        internal abstract bool HistoryLoadFailed { get; }

        // True while a sensor is running on a retry-restored load and nothing has refilled its
        // cache yet: history counts as loaded (self-destroy decides again), but LastValue,
        // Status, IsExpired and the TTL clocks stay unset until the sensor reports. The sweep
        // logs these so a successful retry does not simply drop the sensor out of the failed-
        // load warning and leave the degraded state invisible (#1344).
        internal abstract bool HistoryRestoredByRetry { get; }

        // Bounded rerun of a failed history load (#1344): bypasses Initialize()'s _isInitialized
        // gate (the latch itself is not cleared) at most once per backoff interval, from the
        // maintenance sweep only — never from the per-value paths. The three outcomes are
        // distinct because the sweep budgets on them: only Failed cost a database round trip
        // that is worth rationing.
        internal abstract HistoryLoadRetryResult RetryFailedHistoryLoad(DateTime utcNow);

        // Mirrors the interval states TimeIsUp can actually fire in (None never fires; a Ticks
        // interval with Ticks <= 0 never fires). Used by ShouldDestroy() and by the sweep's
        // deferred/failed-load counters. Value is never null: CustomSettingsProperty falls back
        // through the parent chain to EmptyValue (TimeIntervalModel.None).
        internal bool SelfDestroyIsActive => IsActive(Settings.SelfDestroy.Value);

        private static bool IsActive(TimeIntervalModel interval) =>
            !interval.IsNone && (!interval.UseTicks || interval.Ticks > 0);

        private static DateTime Newest(DateTime a, DateTime b) => a > b ? a : b;

        public bool ShouldDestroy()
        {
            // IsHistoryLoaded means "a load completed", not just "a load was attempted" — a
            // failed load latches _isInitialized but never publishes, so the guard below also
            // covers permanently failed loads (#1328 review). It does NOT mean Storage mirrors
            // history: for a retry-restored sensor it holds only the activity floor, which is
            // all this predicate needs — see HistoryRestoredByRetry before keying anything else
            // on it. The decision is unknown, not "destroy": defer to the next sweep.
            // Deliberately no Initialize() call here — it would run the policy fan-out and let
            // a predicate emit TTL-expired alerts for a sensor this very check may delete.
            var interval = Settings.SelfDestroy.Value;

            if (!IsActive(interval) || !IsHistoryLoaded)
                return false;

            // Storage.To is the newest of the ingestion stamp and the floor a history-load
            // retry restored; MaxValue only for a sensor that never received a value.
            var to = To;
            var storageActivity = to != DateTime.MaxValue ? to : DateTime.MinValue;

            // With a cached value, the newest of it and the storage signal. LastUpdate alone was
            // the hazard: on a sensor whose cache is empty — a retention purge, a full history
            // clear, or a history-load retry, which restores the floor but never the cache —
            // AddValueBase accepts the next value whatever its timestamp, because the
            // newest-wins guard needs a _lastValue to compare against. So one out-of-order value
            // (a reconnecting collector flushing a stale queue) flipped HasData and hid the
            // freshly restored floor behind its own old LastUpdate.
            //
            // The timeout marker stays confined to the empty-cache case, deliberately. It is
            // evidence of when the SERVER noticed the silence, not of sensor activity:
            // GetTimeoutValue stamps Time = UtcNow at observation, which after a maintenance
            // window is the restart instant, not the expiry instant. As the last remaining
            // signal that over-estimate is worth taking (#1328); as a floor under a sensor that
            // still has a cached value it would postpone every quiet sensor's cleanup by the
            // server's downtime. Marker .Time, not .LastUpdateTime: GetTimeoutValue copies
            // LastReceivingTime from the previous value, so LastUpdateTime under-estimates.
            // One acknowledged exception: a history-load retry whose newest DB row is a marker
            // records that .Time as the floor, so for that one sensor the over-estimate does
            // reach this branch through To. Bounded (a single row, once) and conservative.
            var lastActivity = HasData
                ? Newest(LastUpdate, storageActivity)
                : Newest(LastTimeout?.Time ?? DateTime.MinValue, storageActivity);

            // CreationDate is the last resort: no signal at all means the sensor never reported.
            return interval.TimeIsUp(lastActivity != DateTime.MinValue ? lastActivity : CreationDate);
        }

        public bool CanSendNotifications => State is SensorState.Available && (!Status?.IsOfftime ?? true);


        public DateTime LastUpdate => Storage.LastValue?.LastUpdateTime ?? DateTime.MinValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public BaseValue LastTimeout => Storage.LastTimeout;

        public BaseValue LastValue => Storage?.LastValue;


        public bool HasData => Storage.HasData;


        internal Func<Guid, SensorHistoryRequest, ValueTask<List<BaseValue>>> ReadDataFromDb;
        internal Action<SensorEntity> UpdateFromParentSettings;

        public Action<BaseValue> ReceivedNewValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            if (entity.Settings is not null)
                Settings.SetSettings(entity.Settings);

            if (entity.TableSettings is not null)
                TableSettings = new TableSettingsModel(entity.TableSettings);
            
            State = (SensorState)entity.State;
            OriginalUnit = (Unit?)entity.OriginalUnit;
            Integration = (Integration)entity.Integration;
            Statistics = (StatisticsOptions)entity.Statistics;
            AggregateValues = entity.AggregateValues;
            IsSingleton = entity.IsSingleton;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;
            
            if (entity.EnumOptions != null)
            {
                foreach (var option in entity.EnumOptions)
                {
                    EnumOptions.TryAdd(option.Key, new EnumOptionModel(option.Value));
                }
            }

        }

        internal abstract BaseValue GetEmptyValue();

        public void Cut(DateTime time)
        {
            Storage.Cut(time);
        }

        public Task<List<BaseValue>> GetHistoryData(SensorHistoryRequest request) => ReadDataFromDb?.Invoke(Id, request).AsTask() ?? Task.FromResult(new List<BaseValue>());


        protected override void UpdateTTLs(List<PolicyUpdate> updates, InitiatorInfo initiator) => Policies.UpdateTTLs(updates, initiator);

        internal abstract void Revalidate();

        internal abstract bool TryAddValue(BaseValue value);

        internal abstract bool TryUpdateLastValue(BaseValue value);


        internal abstract IEnumerable<BaseValue> Convert(List<byte[]> valuesBytes);

        internal abstract BaseValue Convert(byte[] bytes);

        internal abstract BaseValue ConvertFromJson(string data);

        internal abstract void Initialize();


        internal bool TryUpdate(SensorUpdate update, out string error)
        {
            Update(update);

            TableSettings.MaxCommentHideSize = UpdateProperty(TableSettings.MaxCommentHideSize, update.MaxCommentHideSize ?? TableSettings.MaxCommentHideSize, update.Initiator);
            TableSettings.IsHideEnabled = UpdateProperty(TableSettings.IsHideEnabled, update.IsHideEnabled ?? TableSettings.IsHideEnabled, update.Initiator);
            
            Statistics = UpdateProperty(Statistics, update.Statistics ?? Statistics, update.Initiator);
            Integration = UpdateProperty(Integration, update.Integration ?? Integration, update.Initiator);
            OriginalUnit = UpdateProperty(OriginalUnit, update.SelectedUnit ?? OriginalUnit, update.Initiator, "Unit");
            IsSingleton = UpdateProperty(IsSingleton, update.IsSingleton ?? IsSingleton, update.Initiator, "Singleton");
            AggregateValues = UpdateProperty(AggregateValues, update.AggregateValues ?? AggregateValues, update.Initiator, "Aggregate values");
            DisplayUnit = UpdateProperty(DisplayUnit, update.DisplayUnit ?? DisplayUnit, update.Initiator);

            State = UpdateProperty(State, update.State ?? State, update.Initiator, forced: true, update: update, oldModel: this);
            EndOfMuting = UpdateProperty(EndOfMuting, update.EndOfMutingPeriod, update.Initiator, "End of muting", true);

            if (State == SensorState.Available)
                EndOfMuting = null;

            error = null;

            if (update.Policies != null)
                Policies.TryUpdate(update.Policies, update.Initiator, out error);


            if (update.EnumOptions != null)
            {
                if (EnumOptions.Count == 0 || update.Initiator.IsForceUpdate)
                {
                    foreach (var enumOption in update.EnumOptions)
                    {
                        EnumOptions.TryAdd(enumOption.Key, new EnumOptionModel(enumOption));
                    }
                }
            }

            return string.IsNullOrEmpty(error);
        }


        internal void ResetSensor()
        {
            Policies.Reset();
            Storage.Clear();
        }

        internal SensorEntity ToEntity() => new()
        {
            Id = Id.ToString(),
            AuthorId = AuthorId.ToString(),
            ProductId = Parent.Id.ToString(),
            DisplayName = DisplayName,
            Description = Description,
            CreationDate = CreationDate.Ticks,
            Type = (byte)Type,
            State = (byte)State,
            Statistics = (int)Statistics,
            IsSingleton = IsSingleton,
            Integration = (int)Integration,
            OriginalUnit = (int?)OriginalUnit,
            DisplayUnit = (int?)DisplayUnit,
            AggregateValues = AggregateValues,
            Policies = Policies.Select(u => u.Id.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            Settings = Settings.ToEntity(),
            TTLPolicies = Policies.TTLPolicies.Select(p => p.ToEntity()).ToList(),
            ChangeTable = ChangeTable.ToEntity(),
            EnumOptions = EnumOptions?.ToDictionary(k => k.Key, v => v.Value.ToEntity()),
            TableSettings = TableSettings.ToEntity()
        };

        protected virtual int GetDisplayCoeff()
        {
            return 1;
        }

        public virtual BaseValue ToDisplayValue(BaseValue value) => value;

    }
}