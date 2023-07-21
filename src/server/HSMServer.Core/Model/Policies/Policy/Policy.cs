﻿using HSMCommon.Extensions;
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

        protected BaseSensorModel _sensor;


        public List<PolicyCondition> Conditions { get; } = new();


        public Guid Id { get; private set; }


        internal SensorResult SensorResult { get; private set; }

        internal PolicyResult PolicyResult { get; private set; }

        internal AlertState State { get; private set; }

        internal string Comment { get; private set; }


        public TimeIntervalModel Sensitivity { get; private set; }

        public SensorStatus Status { get; private set; }

        public string Icon { get; private set; }


        public string Template
        {
            get => _userTemplate;
            private set
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


        protected abstract AlertState GetState(BaseValue value);

        protected abstract PolicyCondition GetCondition();


        public string RebuildState(PolicyCondition condition = null, BaseValue value = null)
        {
            State = GetState(value ?? _sensor?.LastValue);
            State.Template = _systemTemplate;

            State.Operation = condition?.Operation.GetDisplayName();
            State.Target = condition?.Target.Value;

            Comment = State.BuildComment();
            SensorResult = new SensorResult(Status, Comment);

            if (_sensor is not null)
                PolicyResult = new PolicyResult(_sensor.Id, this);

            return Comment;
        }

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

        internal void Apply(PolicyEntity entity, BaseSensorModel sensor)
        {
            PolicyCondition Update(PolicyCondition condition, PolicyConditionEntity entity) => condition.FromEntity(entity);

            _sensor = sensor;

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


        protected void ResetState()
        {
            Comment = string.Empty;
            SensorResult = SensorResult.Ok;
            PolicyResult = PolicyResult.Ok;
        }

        private void UpdateConditions<T>(List<T> updates, Func<PolicyCondition, T, PolicyCondition> updateHandler)
        {
            if (updates?.Count > 0)
            {
                Conditions.Clear();

                foreach (var update in updates)
                    Conditions.Add(updateHandler(GetCondition(), update));
            }

            RebuildState();
        }
    }
}