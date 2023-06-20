﻿using HSMDatabase.AccessManager.DatabaseEntities;
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


        internal abstract ValuesStorage Storage { get; }

        public abstract DataPolicyCollection DataPolicies { get; }

        public abstract SensorType Type { get; }


        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public SensorState State { get; private set; }


        public SensorResult? Status => State == SensorState.Muted ? _muteResult : Storage.Result + DataPolicies.SensorResult;

        public PolicyResult PolicyResult => DataPolicies.PolicyResult;

        //public bool IsWaitRestore => !ServerPolicy.CheckRestorePolicies(Status.Status, LastUpdateTime).IsOk;
        public bool IsWaitRestore => false;

        public bool ShouldDestroy => !(Settings.SelfDestroy.Value?.TimeIsUp(LastUpdateTime) ?? true);


        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public BaseValue LastValue => Storage.LastValue;

        public bool HasData => Storage.HasData;


        public Action<BaseValue> ReceivedNewValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            Integration = (Integration)entity.Integration;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;

            DataPolicies.Attach(this);
        }


        internal abstract bool TryAddValue(BaseValue value);

        internal abstract void AddDbValue(byte[] bytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal virtual BaseSensorModel InitDataPolicy() => this;


        internal override bool HasUpdateTimeout() => Settings.HasUpdateTimeout(LastValue?.ReceivingTime);


        internal void Update(SensorUpdate update)
        {
            base.Update(update);

            State = update?.State ?? State;
            Integration = update?.Integration ?? Integration;
            EndOfMuting = update?.EndOfMutingPeriod ?? EndOfMuting;

            if (State == SensorState.Available)
                EndOfMuting = null;

            if (update.DataPolicies != null)
                DataPolicies.Update(update.DataPolicies);
        }

        internal void ResetSensor()
        {
            //ServerPolicy.Reset();
            DataPolicies.Reset();

            Storage.Clear();
        }

        internal override List<Guid> GetPolicyIds() => DataPolicies.Ids.ToList();

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
            Policies = GetPolicyIds().Select(u => u.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            Settings = Settings.ToEntity(),
        };
    }
}
