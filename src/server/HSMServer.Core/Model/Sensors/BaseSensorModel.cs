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
        private readonly ValidationResult _muteStatus = new("Muted", SensorStatus.OffTime);

        protected ValidationResult _internalValidationResult = ValidationResult.Ok;


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public SensorState State { get; private set; }

        public string Unit { get; private set; } //TODO remove


        public DateTime? EndOfMuting { get; private set; }

        public ValidationResult ValidationResult => State == SensorState.Muted ? _muteStatus : _internalValidationResult;


        public BaseValue LastValue => Storage.LastValue;

        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public bool HasData => Storage.HasData;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            State = (SensorState)entity.State;
            Unit = entity.Unit;
            EndOfMuting = entity.EndOfMuting == 0L ? null : new DateTime(entity.EndOfMuting);
        }


        internal override bool HasServerValidationChange()
        {
            //_internalValidationResult -= ExpectedUpdateIntervalPolicy.OutdatedSensor;

            if (!HasData)
                return false;

            var oldValidationResult = _internalValidationResult;

            _internalValidationResult += ServerPolicy.ExpectedUpdate.Policy.Validate(LastValue.ReceivingTime);

            _internalValidationResult += ServerPolicy.RestoreOffTimeStatus.Policy.Validate(DateTime.UtcNow);
            _internalValidationResult += ServerPolicy.RestoreWarningStatus.Policy.Validate(DateTime.UtcNow);
            _internalValidationResult += ServerPolicy.RestoreErrorStatus.Policy.Validate(DateTime.UtcNow);


            return _internalValidationResult != oldValidationResult;
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


        internal SensorEntity ToEntity() =>
            new()
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
                Policies = GetPolicyIds().Select(u => $"{u}").ToList(),
                EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            };


        internal abstract bool TryAddValue(BaseValue value, out BaseValue cachedValue);

        internal abstract void AddValue(byte[] valueBytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal void ClearValues()
        {
            Storage.Clear();
            _internalValidationResult = ValidationResult.Ok;
        }
    }
}
