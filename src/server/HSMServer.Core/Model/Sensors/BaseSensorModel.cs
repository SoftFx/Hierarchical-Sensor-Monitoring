using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Ignored,
        Blocked = byte.MaxValue,
    }


    public interface IBarSensor
    {
        BarBaseValue LocalLastValue { get; }
    }


    public abstract class BaseSensorModel : NodeBaseModel
    {
        private readonly ValidationResult _ignoreStatus = new("Ignored", SensorStatus.OffTime);

        private ValidationResult _curStatus;


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public SensorState State { get; private set; }

        public string Unit { get; private set; }


        public DateTime? EndOfIgnore { get; private set; }

        public ValidationResult ValidationResult
        {
            get => State == SensorState.Ignored ? _ignoreStatus : _curStatus;

            set
            {
                if (value.Result is not SensorStatus.OffTime)
                {
                    _curStatus = value;
                }
            }
        }


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

            var oldValidationResult = ValidationResult;

            ValidationResult += UsedExpectedUpdateInterval.Validate(LastValue);

            return ValidationResult != oldValidationResult;
        }

        internal override void RefreshOutdatedError()
        {
            ValidationResult -= ExpectedUpdateIntervalPolicy.OutdatedSensor;

            CheckExpectedUpdateInterval();
        }


        internal void Update(SensorUpdate update)
        {
            Description = update.Description ?? Description;
            Unit = update.Unit ?? Unit;
            State = update?.State ?? State;
            EndOfIgnore = update?.EndOfIgnorePeriod ?? EndOfIgnore;
           
            if (State == SensorState.Available)
                EndOfIgnore = null;
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
            EndOfIgnore = entity.EndOfIgnore == 0L ? null : new DateTime(entity.EndOfIgnore);

            ValidationResult = ValidationResult.Ok;

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
                EndOfIgnore = EndOfIgnore?.Ticks ?? 0L,
            };


        internal abstract bool TryAddValue(BaseValue value, out BaseValue cachedValue);

        internal abstract void AddValue(byte[] valueBytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal void ClearValues()
        {
            Storage.Clear();
            ValidationResult = ValidationResult.Ok;
        }
    }
}
