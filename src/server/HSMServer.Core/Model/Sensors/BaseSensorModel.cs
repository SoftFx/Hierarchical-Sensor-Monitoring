using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;

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

        protected ValidationResult _internalValidationResult;


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public SensorState State { get; private set; }

        public string Unit { get; private set; }


        public DateTime? EndOfMuting { get; private set; }

        public ValidationResult ValidationResult => State == SensorState.Muted ? _muteStatus : _internalValidationResult;


        public BaseValue LastValue => Storage.LastValue;

        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public bool HasData => Storage.HasData;


        public BaseSensorModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }


        public bool CheckExpectedUpdateInterval()
        {
            if (UsedExpectedUpdateInterval == null || !HasData)
                return false;

            var oldValidationResult = _internalValidationResult;

            _internalValidationResult += UsedExpectedUpdateInterval.Validate(LastValue);

            return _internalValidationResult != oldValidationResult;
        }

        internal override void RefreshOutdatedError()
        {
            _internalValidationResult -= ExpectedUpdateIntervalPolicy.OutdatedSensor;

            CheckExpectedUpdateInterval();
        }


        internal void Update(SensorUpdate update)
        {
            Description = update.Description ?? Description;
            Unit = update.Unit ?? Unit;
            State = update?.State ?? State;
            EndOfMuting = update?.EndOfMutingPeriod ?? EndOfMuting;

            if (State == SensorState.Available)
                EndOfMuting = null;
        }

        internal BaseSensorModel ApplyEntity(SensorEntity entity)
        {
            if (!string.IsNullOrEmpty(entity.Id) && Guid.TryParse(entity.Id, out var entityId))
                Id = entityId;

            if (entity.CreationDate != DateTime.MinValue.Ticks)
                CreationDate = new DateTime(entity.CreationDate);

            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            State = (SensorState)entity.State;
            Unit = entity.Unit;
            EndOfMuting = entity.EndOfMuting == 0L ? null : new DateTime(entity.EndOfMuting);

            _internalValidationResult = ValidationResult.Ok;

            return this;
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
                Policies = GetPolicyIds(),
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
