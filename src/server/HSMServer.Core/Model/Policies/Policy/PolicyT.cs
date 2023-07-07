using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy
    {
        public Guid Id { get; private set; }


        internal protected virtual SensorResult SensorResult { get; protected set; }

        internal protected virtual string AlertComment { get; protected set; }

        internal protected virtual AlertState State { get; protected set; }


        public virtual SensorStatus Status { get; protected set; }

        public virtual string Template { get; protected set; }

        public virtual string Icon { get; protected set; }


        public virtual PolicyOperation Operation { get; set; }

        public virtual TargetValue Target { get; set; }

        public virtual string Property { get; set; }


        public Policy()
        {
            Id = Guid.NewGuid();
        }


        internal void Update(DataPolicyUpdate update)
        {
            Operation = update.Operation;
            Property = update.Property;
            Template = update.Template;
            Target = update.Target;
            Status = update.Status;
            Icon = update.Icon;
        }

        internal void Apply(PolicyEntity entity)
        {
            Id = new Guid(entity.Id);

            Operation = (PolicyOperation)entity.Operation;
            Status = (SensorStatus)entity.SensorStatus;

            Property = entity.Property;
            Template = entity.Template;
            Icon = entity.Icon;

            Target = new TargetValue((TargetType)entity.Target.Type, entity.Target.Value);
        }

        internal PolicyEntity ToEntity() => new()
        {
            Id = Id.ToByteArray(),

            Operation = (byte)Operation,
            SensorStatus = (byte)Status,

            Property = Property,
            Template = Template,
            Icon = Icon,

            Target = new((byte)Target.Type, Target.Value)
        };
    }


    public abstract class Policy<T> : Policy where T : BaseValue
    {
        private AlertSystemTemplate _systemTemplate;
        private string _userTemplate;


        public override string Template
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


        protected abstract AlertState GetState(T value, BaseSensorModel sensor);

        internal abstract bool Validate(T value, BaseSensorModel sensor);


        public string BuildStateAndComment(T value, BaseSensorModel sensor)
        {
            State = GetState(value, sensor);
            AlertComment = State.BuildComment();

            return AlertComment;
        }

        protected AlertState FillPolicyState(AlertState state)
        {
            state.Operation = Operation.GetDisplayName();
            state.Target = Target.Value;

            state.Template = _systemTemplate;

            return state;
        }
    }
}