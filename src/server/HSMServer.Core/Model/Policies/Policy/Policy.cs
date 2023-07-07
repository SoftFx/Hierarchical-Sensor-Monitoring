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


        public List<PolicyCondition> Conditions { get; }


        internal protected virtual SensorResult SensorResult { get; protected set; }

        internal protected virtual AlertState State { get; protected set; }

        internal protected virtual string AlertComment { get; protected set; }



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
            if (update.Conditions != null)
            {
                Conditions.Clear();

                foreach (var condUpdate in update.Conditions)
                {
                    var newCond = GetCondition();

                    newCond.Combination = condUpdate.Combination;
                    newCond.Operation = condUpdate.Operation;
                    newCond.Target = condUpdate.Target;
                    newCond.Property = condUpdate.Property;

                    Conditions.Add(newCond);
                }
            }

            Template = update.Template;
            Status = update.Status;
            Icon = update.Icon;
        }

        internal void Apply(PolicyEntity entity)
        {
            Id = new Guid(entity.Id);


            if (entity.Conditions != null)
            {
                Conditions.Clear();

                foreach (var condEntity in entity.Conditions)
                    Conditions.Add(GetCondition().FromEntity(condEntity));
            }

            Status = (SensorStatus)entity.SensorStatus;

            Template = entity.Template;
            Icon = entity.Icon;
        }

        internal PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),

            Conditions = Conditions?.Select(u => u.ToEntity()).ToList(),

            SensorStatus = (byte)Status,
            Template = Template,
            Icon = Icon,
        };
    }
}
