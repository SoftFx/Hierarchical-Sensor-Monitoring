using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.DataLayer;
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


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public DateTime CreationDate { get; }

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


        private BaseSensorModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        internal BaseSensorModel(string productId, string sensorName) : this()
        {
            ProductId = productId;
            DisplayName = sensorName;
        }

        internal BaseSensorModel(SensorEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.TryParse(entity.AuthorId, out var authorId) ? authorId : null;
            ProductId = entity.ProductId;
            CreationDate = new DateTime(entity.CreationDate);
            DisplayName = entity.DisplayName;
            Description = entity.Description;
            State = (SensorState)entity.State;
            Unit = entity.Unit;
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

        internal void AddValue(BaseValue value) => Storage.AddValue(value);

        internal void AddValue(byte[] valueBytes) => Storage.AddValue(valueBytes);

        internal void ClearValues() => Storage.Clear();


        internal static BaseSensorModel GetModel(SensorEntity entity, IDatabaseCore db) =>
            (SensorType)entity.Type switch
            {
                SensorType.Boolean => new BooleanSensorModel(entity, db),
                SensorType.Integer => new IntegerSensorModel(entity, db),
                SensorType.Double => new DoubleSensorModel(entity, db),
                SensorType.String => new StringSensorModel(entity, db),
                SensorType.IntegerBar => new IntegerBarSensorModel(entity, db),
                SensorType.DoubleBar => new DoubleBarSensorModel(entity, db),
                SensorType.File => new FileSensorModel(entity, db),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };

        internal static BaseSensorModel GetModel(BaseValue value, string productId, string name) =>
            value switch
            {
                BooleanValue => new BooleanSensorModel(productId, name),
                IntegerValue => new IntegerSensorModel(productId, name),
                DoubleValue => new DoubleSensorModel(productId, name),
                StringValue => new StringSensorModel(productId, name),
                IntegerBarValue => new IntegerBarSensorModel(productId, name),
                DoubleBarValue => new DoubleBarSensorModel(productId, name),
                FileValue => new FileSensorModel(productId, name),
                _ => throw new ArgumentException($"Unexpected sensor value type {value.GetType()}"),
            };
    }


    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _userPolicies = new();


        protected override ValuesStorage<T> Storage { get; }


        internal BaseSensorModel(string productId, string sensorName) : base(productId, sensorName) { }

        internal BaseSensorModel(SensorEntity entity) : base(entity) { }


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
