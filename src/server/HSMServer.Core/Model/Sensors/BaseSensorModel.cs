using HSMDatabase.AccessManager.DatabaseEntities;
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


    public abstract class BaseSensorModel : NodeBaseModel
    {
        private static readonly ValidationResult _muteResult = new("Muted", SensorStatus.OffTime);

        private ValidationResult _serverResult = ValidationResult.Ok;
        protected ValidationResult _dataResult = ValidationResult.Ok;

        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public SensorState State { get; private set; }

        public string Unit { get; private set; } //TODO remove


        public DateTime? EndOfMuting { get; private set; }

        public ValidationResult ValidationResult => State == SensorState.Muted ? _muteResult : _serverResult + _dataResult;


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


        internal override bool HasServerValidationChange()
        {
            _serverResult = ValidationResult.Ok;

            if (!HasData)
                return false;

            var oldResult = _serverResult;

            _serverResult += ServerPolicy.ExpectedUpdate.Policy.Validate(LastValue.ReceivingTime);
            _serverResult += ServerPolicy.CheckRestorePolicies(DateTime.UtcNow);

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


        internal abstract bool TryAddValue(BaseValue value);

        internal abstract void AddValue(byte[] valueBytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal void ResetSensor()
        {
            _serverResult = ValidationResult.Ok;
            _dataResult = ValidationResult.Ok;

            Storage.Clear();
        }


        internal SensorEntity ToEntity() => new()
        {
            Id = Id.ToString(),
            AuthorId = AuthorId.ToString(),
            ProductId = ParentProduct.Id.ToString(),
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
