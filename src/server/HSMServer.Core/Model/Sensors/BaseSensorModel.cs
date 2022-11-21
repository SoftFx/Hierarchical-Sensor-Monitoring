using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Freezed,
        Blocked = byte.MaxValue,
    }


    internal interface IBarSensor
    {
        BarBaseValue LocalLastValue { get; }
    }


    public abstract class BaseSensorModel : NodeBaseModel
    {
        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public Guid Id { get; private set; }

        public SensorState State { get; private set; }

        public string Unit { get; private set; }

        public ValidationResult ValidationResult { get; protected set; }


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
            if (UsedExpectedUpdateIntervalPolicy == null || !HasData)
                return false;

            var oldValidationResult = ValidationResult;

            ValidationResult += UsedExpectedUpdateIntervalPolicy.Validate(LastValue);

            return ValidationResult != oldValidationResult;
        }

        internal override void UpdateExpectedUpdateIntervalError()
        {
            ValidationResult -= ExpectedUpdateIntervalPolicy.OutdatedSensor;

            CheckExpectedUpdateInterval();
        }


        internal override void BuildProductNameAndPath()
        {
            base.BuildProductNameAndPath();

            Path = $"{ParentProduct.Path}{DisplayName}";
        }


        internal void Update(SensorUpdate sensor)
        {
            Description = sensor.Description ?? Description;
            Unit = sensor.Unit ?? Unit;
            State = sensor?.State ?? State;
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

            ValidationResult = ValidationResult.Ok;

            return this;
        }

        internal SensorEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId.ToString(),
                ProductId = ParentProduct.Id,
                DisplayName = DisplayName,
                Description = Description,
                Unit = Unit,
                CreationDate = CreationDate.Ticks,
                Type = (byte)Type,
                State = (byte)State,
                Policies = GetPolicyIds(),
            };


        internal abstract bool TryAddValue(BaseValue value, out BaseValue cachedValue);

        internal abstract void AddValue(byte[] valueBytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal void ClearValues()
        {
            Storage.Clear();
            ValidationResult = ValidationResult.Ok;
        }

        internal List<BaseValue> GetValues(int count) => Storage.GetValues(count);

        internal List<BaseValue> GetValues(DateTime from, DateTime to) => Storage.GetValues(from, to);
    }
}
