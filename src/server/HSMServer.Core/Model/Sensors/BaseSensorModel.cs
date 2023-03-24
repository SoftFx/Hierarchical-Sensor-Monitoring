﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
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


    public interface IBarSensor
    {
        BarBaseValue LocalLastValue { get; }
    }


    public abstract class BaseSensorModel : BaseNodeModel
    {
        private static readonly PolicyResult _muteResult = new(SensorStatus.OffTime, "Muted");

        private PolicyResult _serverResult = PolicyResult.Ok;
        protected PolicyResult _dataResult = PolicyResult.Ok;


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public DateTime? EndOfMuting { get; private set; }

        public SensorState State { get; private set; }

        public string Unit { get; private set; } //TODO remove


        public bool IsWaitRestore=> !ServerPolicy.CheckRestorePolicies(Status.Status, LastUpdateTime).IsOk;

        public PolicyResult Status => State == SensorState.Muted ? _muteResult : _serverResult + _dataResult;


        public bool HasData => Storage.HasData;

        public BaseValue LastValue => Storage.LastValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            Unit = entity.Unit;
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

            Unit = update.Unit ?? Unit;
            State = update?.State ?? State;
            EndOfMuting = update?.EndOfMutingPeriod ?? EndOfMuting;

            if (State == SensorState.Available)
                EndOfMuting = null;
        }

        internal void ResetSensor()
        {
            _serverResult = PolicyResult.Ok;
            _dataResult = PolicyResult.Ok;

            Storage.Clear();
        }

        internal SensorEntity ToEntity() => new()
        {
            Id = Id.ToString(),
            AuthorId = AuthorId.ToString(),
            ProductId = Parent.Id.ToString(),
            DisplayName = DisplayName,
            Description = Description,
            Unit = Unit,
            CreationDate = CreationDate.Ticks,
            Type = (byte)Type,
            State = (byte)State,
            Policies = GetPolicyIds().Select(u => u.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
        };
    }
}
