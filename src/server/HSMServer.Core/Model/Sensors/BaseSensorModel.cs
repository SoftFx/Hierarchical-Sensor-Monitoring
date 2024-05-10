using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
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


    public abstract class BaseSensorModel : BaseNodeModel
    {
        private static readonly SensorResult _muteResult = new(SensorStatus.OffTime, "Muted");

        public override SensorPolicyCollection Policies { get; }


        internal abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public bool IsSingleton { get; private set; }

        public bool AggregateValues { get; private set; }

        public StatisticsOptions Statistics { get; private set; }

        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public Unit? OriginalUnit { get; private set; }

        public SensorState State { get; private set; }


        public SensorResult? Status
        {
            get
            {
                if (State == SensorState.Muted)
                    return _muteResult;

                return !Policies.SensorResult.IsOk ? Policies.SensorResult : Storage.Result;
            }
        }


        public PolicyResult Notifications => Policies.NotificationResult;

        public PolicyResult PolicyResult => Policies.PolicyResult;


        public bool ShouldDestroy => Settings.SelfDestroy.Value?.TimeIsUp(HasData ? LastUpdate : CreationDate) ?? false;

        public bool CanSendNotifications => State is SensorState.Available && (!Status?.IsOfftime ?? true);


        public DateTime LastUpdate => Storage.LastValue?.LastUpdateTime ?? DateTime.MinValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public BaseValue LastTimeout => Storage.LastTimeout;

        public BaseValue LastValue => Storage.LastValue;


        public bool HasData => Storage.HasData;


        internal Func<Guid, SensorHistoryRequest, ValueTask<List<BaseValue>>> ReadDataFromDb;
        internal Action<SensorEntity> UpdateFromParentSettings;

        public Action<BaseValue> ReceivedNewValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            OriginalUnit = (Unit?)entity.OriginalUnit;
            Integration = (Integration)entity.Integration;
            Statistics = (StatisticsOptions)entity.Statistics;
            AggregateValues = entity.AggregateValues;
            IsSingleton = entity.IsSingleton;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;
        }


        public Task<List<BaseValue>> GetHistoryData(SensorHistoryRequest request) => ReadDataFromDb?.Invoke(Id, request).AsTask() ?? Task.FromResult(new List<BaseValue>());


        protected override void UpdateTTL(PolicyUpdate update) => Policies.UpdateTTL(update);

        internal abstract void Revalidate();

        internal abstract bool TryAddValue(BaseValue value);

        internal abstract void AddDbValue(byte[] bytes);

        internal abstract bool TryUpdateLastValue(BaseValue value);


        internal abstract IEnumerable<BaseValue> Convert(List<byte[]> valuesBytes);

        internal abstract BaseValue Convert(byte[] bytes);


        internal bool TryUpdate(SensorUpdate update, out string error)
        {
            Update(update);

            Statistics = UpdateProperty(Statistics, update.Statistics ?? Statistics, update.Initiator);
            Integration = UpdateProperty(Integration, update.Integration ?? Integration, update.Initiator);
            OriginalUnit = UpdateProperty(OriginalUnit, update.SelectedUnit ?? OriginalUnit, update.Initiator, "Unit");
            IsSingleton = UpdateProperty(IsSingleton, update.IsSingleton ?? IsSingleton, update.Initiator, "Singleton");
            AggregateValues = UpdateProperty(AggregateValues, update.AggregateValues ?? AggregateValues, update.Initiator, "Aggregate values");

            State = UpdateProperty(State, update.State ?? State, update.Initiator, forced: true, update: update, oldModel: this);
            EndOfMuting = UpdateProperty(EndOfMuting, update.EndOfMutingPeriod, update.Initiator, "End of muting", true);

            if (State == SensorState.Available)
                EndOfMuting = null;

            error = null;

            if (update.Policies != null)
                Policies.TryUpdate(update.Policies, update.Initiator, out error);

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
            AggregateValues = AggregateValues,
            Policies = Policies.Select(u => u.Id.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            Settings = Settings.ToEntity(),
            DefaultChatsSettings = Settings.DefaultChats.ToEntity(),
            TTLPolicy = Policies.TimeToLive?.ToEntity(),
            ChangeTable = ChangeTable.ToEntity(),
        };
    }
}