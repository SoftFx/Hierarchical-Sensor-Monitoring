using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy
    {
        private AlertSystemTemplate _systemTemplate;
        private string _userTemplate;


        public Guid Id { get; private set; }


        internal protected virtual SensorResult SensorResult { get; protected set; }

        internal protected virtual string AlertComment { get; protected set; }


        public List<PolicyCondition> Conditions { get; } = new();

        public AlertState State { get; private set; }


        public virtual TimeIntervalModel Sensitivity { get; protected set; }

        public virtual SensorStatus Status { get; protected set; }

        public virtual string Icon { get; protected set; }


        public string Template
        {
            get => _userTemplate;
            protected set
            {
                if (_userTemplate == value)
                    return;

                _userTemplate = value;
                _systemTemplate = AlertState.BuildSystemTemplate(value);
            }
        }


        public Policy()
        {
            Id = Guid.NewGuid();
        }


        public abstract string BuildStateAndComment(BaseValue value, BaseSensorModel sensor, PolicyCondition condition);

        protected abstract PolicyCondition GetCondition();


        internal void Update(PolicyUpdate update)
        {
            PolicyCondition Update(PolicyCondition condition, PolicyConditionUpdate update)
            {
                condition.Combination = update.Combination;
                condition.Operation = update.Operation;
                condition.Property = update.Property;
                condition.Target = update.Target;

                return condition;
            }

            Sensitivity = update.Sensitivity;
            Template = update.Template;
            Status = update.Status;
            Icon = update.Icon;

            UpdateConditions(update.Conditions, Update);
        }

        internal void Apply(PolicyEntity entity)
        {
            PolicyCondition Update(PolicyCondition condition, PolicyConditionEntity entity) => condition.FromEntity(entity);

            Id = new Guid(entity.Id);
            Status = entity.SensorStatus.ToStatus();

            Template = entity.Template;
            Icon = entity.Icon;

            if (entity.Sensitivity is not null)
                Sensitivity = new TimeIntervalModel(entity.Sensitivity);

            UpdateConditions(entity.Conditions, Update);
        }

        internal PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),

            Conditions = Conditions?.Select(u => u.ToEntity()).ToList(),

            Sensitivity = Sensitivity?.ToEntity(),
            SensorStatus = (byte)Status,
            Template = Template,
            Icon = Icon,
        };


        protected string SetStateAndGetComment(AlertState state)
        {
            State = state;
            State.Template = _systemTemplate;

            AlertComment = State.BuildComment();

            return AlertComment;
        }


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