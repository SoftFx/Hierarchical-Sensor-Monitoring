using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
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


    public abstract class BaseSensorModel
    {
        protected readonly List<Policy> _systemPolicies = new();


        protected abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public Guid Id { get; private set; }

        public Guid? AuthorId { get; private set; }

        public DateTime CreationDate { get; private set; }

        public string ProductId { get; private set; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public SensorState State { get; private set; }

        // TODO: Status & DataError -> ValidationResult
        //public SensorStatus Status { get; private set; }
        //public string DataError { get; private set; }

        public string Unit { get; private set; }

        public ExpectedUpdateIntervalPolicy ExpectedUpdateIntervalPolicy { get; private set; }

        public string ProductName { get; private set; }

        public string Path { get; private set; }


        public string LatestValueInfo => Storage.LatestValueInfo;

        public DateTime LastUpdateTime => Storage.LastUpdateTime;

        public bool HasData => Storage.HasData;


        public BaseSensorModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }


        internal abstract SensorEntity ToEntity();

        internal virtual void AddPolicy(Policy policy)
        {
            _systemPolicies.Add(policy);

            if (policy is ExpectedUpdateIntervalPolicy expectedUpdateIntervalPolicy)
                ExpectedUpdateIntervalPolicy = expectedUpdateIntervalPolicy;
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

        internal bool AddValue(BaseValue value) => Storage.AddValue(value);

        internal void AddValue(byte[] valueBytes) => Storage.AddValue(valueBytes);

        internal void ClearValues() => Storage.Clear();


        internal static BaseSensorModel GetModel(SensorEntity entity)
        {
            BaseSensorModel BuildSensor<T>() where T : BaseSensorModel, new() =>
                new T().ApplyEntity(entity);

            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => BuildSensor<BooleanSensorModel>(),
                SensorType.Integer => BuildSensor<IntegerSensorModel>(),
                SensorType.Double => BuildSensor<DoubleSensorModel>(),
                SensorType.String => BuildSensor<StringSensorModel>(),
                SensorType.IntegerBar => BuildSensor<IntegerBarSensorModel>(),
                SensorType.DoubleBar => BuildSensor<DoubleBarSensorModel>(),
                SensorType.File => BuildSensor<FileSensorModel>(),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }

        internal static BaseSensorModel GetModel(BaseValue value, string productId, string name)
        {
            BaseSensorModel BuildSensor<T>() where T : BaseSensorModel, new()
            {
                SensorEntity entity = new()
                {
                    ProductId = productId,
                    DisplayName = name,
                };

                return new T().ApplyEntity(entity);
            }

            return value switch
            {
                BooleanValue => BuildSensor<BooleanSensorModel>(),
                IntegerValue => BuildSensor<IntegerSensorModel>(),
                DoubleValue => BuildSensor<DoubleSensorModel>(),
                StringValue => BuildSensor<StringSensorModel>(),
                IntegerBarValue => BuildSensor<IntegerBarSensorModel>(),
                DoubleBarValue => BuildSensor<DoubleBarSensorModel>(),
                FileValue => BuildSensor<FileSensorModel>(),
                _ => throw new ArgumentException($"Unexpected sensor value type {value.GetType()}"),
            };
        }

        private BaseSensorModel ApplyEntity(SensorEntity entity)
        {
            var entityId = Guid.Parse(entity.Id);
            if (entityId != Guid.Empty)
                Id = entityId;

            if (entity.CreationDate != DateTime.MinValue.Ticks)
                CreationDate = new DateTime(entity.CreationDate);

            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            ProductId = entity.ProductId;
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            State = (SensorState)entity.State;
            Unit = entity.Unit;

            return this;
        }
    }


    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _userPolicies = new();


        protected override ValuesStorage<T> Storage { get; }


        internal override void AddPolicy(Policy policy)
        {
            if (policy is Policy<T> customPolicy)
                _userPolicies.Add(customPolicy);
            else
                base.AddPolicy(policy);
        }

        internal override SensorEntity ToEntity() =>
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
                Policies = GetPolicyIds(),
            };

        private List<string> GetPolicyIds()
        {
            var policyIds = new List<string>(_systemPolicies.Count + _userPolicies.Count);

            policyIds.AddRange(_systemPolicies.Select(p => p.Id.ToString()));
            policyIds.AddRange(_userPolicies.Select(p => p.Id.ToString()));

            return policyIds;
        }
    }
}
