using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
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
        private static readonly PolicyResult _muteResult = new(SensorStatus.OffTime, "Muted");

        private PolicyResult _serverResult = PolicyResult.Ok;


        protected abstract ValuesStorage Storage { get; }

        public abstract DataPolicyCollection DataPolicies { get; }

        public abstract SensorType Type { get; }


        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public SensorState State { get; private set; }


        public bool IsWaitRestore => !ServerPolicy.CheckRestorePolicies(Status.Status, LastUpdateTime).IsOk;

        public PolicyResult Status => State == SensorState.Muted ? _muteResult : _serverResult + DataPolicies.Result;


        public bool HasData => Storage.HasData;

        public BaseValue LastValue => Storage.LastValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            Integration = (Integration)entity.Integration;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;
        }


        internal abstract bool TryAddValue(BaseValue value);

        internal abstract bool TryAddValue(byte[] bytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal virtual BaseSensorModel InitDataPolicy() => this;


        internal override bool HasUpdateTimeout()
        {
            var oldResult = _serverResult;

            _serverResult -= ServerPolicy.ExpectedUpdate.Policy.Fail;

            if (!HasData)
                return false;

            _serverResult += ServerPolicy.ExpectedUpdate.Policy.Validate(LastValue.ReceivingTime);

            return _serverResult != oldResult;
        }


        internal void Update(SensorUpdate update)
        {
            base.Update(update);

            State = update?.State ?? State;
            Integration = update?.Integration ?? Integration;
            EndOfMuting = update?.EndOfMutingPeriod ?? EndOfMuting;

            if (State == SensorState.Available)
                EndOfMuting = null;

            DataPolicies.Update(update.DataPolicies);
        }

        internal void ResetSensor()
        {
            _serverResult = PolicyResult.Ok;
            DataPolicies.Reset();

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
            Policies = GetPolicyIds().AddRangeFluent(DataPolicies.Ids).Select(u => u.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
        };
    }
}
