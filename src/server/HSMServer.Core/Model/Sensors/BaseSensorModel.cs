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
        protected readonly List<Policy> _systemPolicies = new();


        public Guid Id { get; }

        public Guid? AuthorId { get; }

        public string ProductId { get; }

        public DateTime CreationDate { get; }

        public abstract SensorType Type { get; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        public SensorState State { get; private set; }

        // TODO: Status & DataError -> ValidationResult
        //public SensorStatus Status { get; private set; }
        //public string DataError { get; private set; }

        public string Unit { get; private set; }

        // TODO: maybe store in Storage
        public DateTime LastUpdateTime { get; private set; }

        public ExpectedUpdateIntervalPolicy ExpectedUpdateIntervalPolicy { get; private set; }


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


        internal virtual void AddPolicy(Policy policy)
        {
            _systemPolicies.Add(policy);

            if (policy is ExpectedUpdateIntervalPolicy expectedUpdateIntervalPolicy)
                ExpectedUpdateIntervalPolicy = expectedUpdateIntervalPolicy;
        }

        internal abstract SensorEntity ToEntity();
    }


    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _userPolicies = new();


        public abstract ValuesStorage<T> Storage { get; }


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
