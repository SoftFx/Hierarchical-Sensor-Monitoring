using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Muted,
        Blocked = byte.MaxValue,
    }

    [Flags]
    public enum Integration : int
    {
        Grafana = 1,
    }


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


        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public SensorState State { get; private set; }


        public SensorResult? Status => State == SensorState.Muted ? _muteResult : Storage.Result + Policies.SensorResult;

        public PolicyResult PolicyResult => Policies.PolicyResult;

        public bool ShouldDestroy => Settings.SelfDestroy.Value?.TimeIsUp(LastUpdate) ?? false;


        public DateTime LastUpdate => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public BaseValue LastValue => Storage.LastValue;

        public bool HasData => Storage.HasData;


        public Action<BaseValue> ReceivedNewValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            Integration = (Integration)entity.Integration;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;

            Policies.Attach(this);
        }


        internal abstract bool TryAddValue(BaseValue value);

        internal abstract void AddDbValue(byte[] bytes);

        internal abstract void RecalculatePolicy();

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);


        public void Update(SensorUpdate update)
        {
            base.Update(update);

            State = ApplyUpdate(State, update?.State ?? State);
            Integration = ApplyUpdate(Integration, update?.Integration ?? Integration);
            EndOfMuting = ApplyUpdate(EndOfMuting, update?.EndOfMutingPeriod ?? EndOfMuting);

            if (State == SensorState.Available)
                EndOfMuting = null;

            if (update.DataPolicies != null)
                Policies.Update(update.DataPolicies);
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
            Integration = (int)Integration,
            Policies = Policies.Ids.Select(u => u.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            Settings = Settings.ToEntity(),
        };
    }
}