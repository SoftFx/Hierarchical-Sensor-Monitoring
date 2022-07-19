﻿using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.SensorsDataValidation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Freezed,
        Blocked = byte.MaxValue,
    }


    public abstract class BaseSensorModel : IDisposable
    {
        protected readonly List<Policy> _policies = new();


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public Guid Id { get; private set; }

        public Guid? AuthorId { get; private set; }

        public DateTime CreationDate { get; private set; }

        public string ProductId { get; private set; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public SensorState State { get; private set; }

        public string Unit { get; private set; }

        public ExpectedUpdateIntervalPolicy ExpectedUpdateIntervalPolicy { get; private set; }

        public string ProductName { get; private set; }

        public string Path { get; private set; }

        public ValidationResult ValidationResult { get; protected set; }


        public BaseValue LastValue => Storage.LastValue;

        public DateTime LastUpdateTime => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public bool HasData => Storage.HasData;


        public BaseSensorModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }


        public bool CheckExpectedUpdateInterval(BaseValue value)
        {
            if (ExpectedUpdateIntervalPolicy == null || value == null)
                return false;

            var validationMessage = ValidationResult.Message;

            ValidationResult += ExpectedUpdateIntervalPolicy.Validate(value);

            return ValidationResult.Message != validationMessage;
        }


        internal void BuildProductNameAndPath(ProductModel parentProduct)
        {
            var pathParts = new List<string>() { DisplayName };

            while (parentProduct.ParentProduct != null)
            {
                pathParts.Add(parentProduct.DisplayName);
                parentProduct = parentProduct.ParentProduct;
            }

            pathParts.Reverse();

            Path = string.Join(CommonConstants.SensorPathSeparator, pathParts);
            ProductName = parentProduct.DisplayName;
        }

        internal void Update(SensorUpdate sensor)
        {
            Description = sensor.Description;
            //ExpectedUpdateInterval = TimeSpan.Parse(sensor.ExpectedUpdateInterval); // TODO update expected update interval policy!!!
            Unit = sensor.Unit;
        }


        internal BaseSensorModel ApplyEntity(SensorEntity entity)
        {
            if (!string.IsNullOrEmpty(entity.Id) && Guid.TryParse(entity.Id, out var entityId))
                Id = entityId;

            if (entity.CreationDate != DateTime.MinValue.Ticks)
                CreationDate = new DateTime(entity.CreationDate);

            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            ProductId = entity.ProductId;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            State = (SensorState)entity.State;
            Unit = entity.Unit;

            ValidationResult = PredefinedValidationResults.Success;

            return this;
        }

        internal SensorEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                AuthorId = AuthorId.ToString(),
                ProductId = ProductId,
                DisplayName = DisplayName,
                Description = Description,
                Unit = Unit,
                CreationDate = CreationDate.Ticks,
                Type = (byte)Type,
                State = (byte)State,
                Policies = _policies.Select(p => p.Id.ToString()).ToList(),
            };


        internal abstract bool TryAddValue(BaseValue value, out BaseValue cachedValue);

        internal abstract void AddValue(byte[] valueBytes);

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);

        internal void ClearValues() => Storage.Clear();

        internal List<BaseValue> GetValues(int count) => Storage.GetValues(count);

        internal List<BaseValue> GetValues(DateTime from, DateTime to) => Storage.GetValues(from, to);


        internal virtual void AddPolicy(Policy policy)
        {
            _policies.Add(policy);

            if (policy is ExpectedUpdateIntervalPolicy expectedUpdateIntervalPolicy)
                ExpectedUpdateIntervalPolicy = expectedUpdateIntervalPolicy;
        }


        public void Dispose() => Storage.Dispose();
    }
}
