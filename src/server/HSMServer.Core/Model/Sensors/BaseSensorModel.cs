using HSMDatabase.AccessManager.DatabaseEntities;
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
        protected readonly List<Policy> _basePolicies = new();


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public string ProductId { get; }

        public DateTime CreationDate { get; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        //public SensorType Type { get; private set; }

        public SensorState State { get; private set; }

        // TODO: Status & DataError -> ValidationResult
        //public SensorStatus Status { get; private set; }
        //public string DataError { get; private set; }

        public string Unit { get; private set; }

        // TODO: maybe store in Storage
        public DateTime LastUpdateTime { get; private set; }


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


        internal void AddBasePolicy(Policy policy) =>
            _basePolicies.Add(policy);

        internal abstract void AddCustomPolicy(Policy policy);

        internal abstract SensorEntity ToEntity();
    }


    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _customPolicies = new();


        public abstract ValuesStorage<T> Storage { get; }


        internal BaseSensorModel(SensorEntity entity) : base(entity) { }


        internal override void AddCustomPolicy(Policy policy) =>
            _customPolicies.Add((Policy<T>)policy);

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
                Type = (byte)GetSensorType(),
                State = (byte)State,
                Policies = GetPolicyIds(),
            };

        // Можно вместо switch хранить Type в базовой моделе.
        private SensorType GetSensorType() =>
            this switch
            {
                BooleanSensorModel => SensorType.Boolean,
                IntegerSensorModel => SensorType.Integer,
                DoubleSensorModel => SensorType.Double,
                StringSensorModel => SensorType.String,
                FileSensorModel => SensorType.File,
                IntegerBarSensorModel => SensorType.IntegerBar,
                DoubleBarSensorModel => SensorType.DoubleBar,
            };

        private List<string> GetPolicyIds()
        {
            var policyIds = new List<string>(_basePolicies.Count + _customPolicies.Count);

            policyIds.AddRange(_basePolicies.Select(p => p.Id.ToString()));
            policyIds.AddRange(_customPolicies.Select(p => p.Id.ToString()));

            return policyIds;
        }
    }
}
