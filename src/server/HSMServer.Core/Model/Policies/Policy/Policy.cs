using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy
    {
        public Guid Id { get; private set; }


        internal protected virtual SensorResult SensorResult { get; protected set; }

        internal protected virtual AlertState State { get; protected set; }

        internal protected virtual string AlertComment { get; protected set; }


        public List<PolicyCondition> Conditions { get; } = new();

        public virtual TimeIntervalModel Sensativity { get; protected set; }

        public virtual SensorStatus Status { get; protected set; }

        public virtual string Template { get; protected set; }

        public virtual string Icon { get; protected set; }


        public Policy()
        {
            Id = Guid.NewGuid();
        }


        protected abstract PolicyCondition GetCondition();

        internal void Update(DataPolicyUpdate update)
        {
            PolicyCondition Update(PolicyCondition condition, PolicyConditionUpdate update)
            {
                condition.Combination = update.Combination;
                condition.Operation = update.Operation;
                condition.Target = update.Target;
                condition.Property = update.Property;

                return condition;
            }

            Sensativity = update.Sensativity;
            Template = update.Template;
            Status = update.Status;
            Icon = update.Icon;

            UpdateConditions(update.Conditions, Update);
        }

        internal void Apply(PolicyEntity entity)
        {
            PolicyCondition Update(PolicyCondition condition, PolicyConditionEntity entity) => condition.FromEntity(entity);

            Id = new Guid(entity.Id);
            Status = (SensorStatus)entity.SensorStatus;

            Sensativity = new TimeIntervalModel(entity.Sensativity);
            Template = entity.Template;
            Icon = entity.Icon;

            UpdateConditions(entity.Conditions, Update);
        }

        internal PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),

            Conditions = Conditions?.Select(u => u.ToEntity()).ToList(),

            Sensativity = Sensativity?.ToEntity(),
            SensorStatus = (byte)Status,
            Template = Template,
            Icon = Icon,
        };

        private void UpdateConditions<T>(List<T> updates, Func<PolicyCondition, T, PolicyCondition> updateHandler)
        {
            if (updates?.Count > 0)
            {
                Conditions.Clear();

                foreach (var update in updates)
                    Conditions.Add(updateHandler(GetCondition(), update));
            }
        }
    }
}