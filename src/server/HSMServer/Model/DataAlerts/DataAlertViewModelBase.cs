﻿using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class DataAlertViewModelBase
    {
        public List<AlertConditionBase> Conditions { get; } = new();

        public List<AlertActionBase> Actions { get; } = new();


        public Guid EntityId { get; set; }

        public Guid Id { get; set; }


        public bool IsModify { get; protected set; }


        internal PolicyUpdate ToUpdate()
        {
            List<PolicyConditionUpdate> conditions = new(Conditions.Count);
            Core.Model.TimeIntervalModel sensitivity = null;

            foreach (var condition in Conditions)
            {
                if (condition.Property == AlertProperty.TimeToLive)
                    continue;

                if (condition.Property == AlertProperty.Sensitivity)
                {
                    sensitivity = condition.Sensitivity.ToModel();
                    continue;
                }

                var target = condition.Property == AlertProperty.Status
                    ? new TargetValue(TargetType.LastValue, EntityId.ToString())
                    : new TargetValue(TargetType.Const, condition.Target);

                conditions.Add(new PolicyConditionUpdate(condition.Operation, condition.Property.ToCore(), target));
            }

            SensorStatus status = SensorStatus.Ok;
            string comment = null;
            string icon = null;

            foreach (var action in Actions)
            {
                if (action.Action == ActionType.SendNotification)
                    comment = action.Comment;
                else if (action.Action == ActionType.ShowIcon)
                    icon = action.Icon;
                else if (action.Action == ActionType.SetStatus)
                    status = SensorStatus.Error;
            }


            return new(Id, conditions, sensitivity, status.ToCore(), comment, icon);
        }
    }


    public abstract class DataAlertViewModelBase<T> : DataAlertViewModelBase where T : Core.Model.BaseValue
    {
        public DataAlertViewModelBase(Guid entityId)
        {
            EntityId = entityId;
            IsModify = true;

            Actions.Add(new ActionViewModel(true));
        }

        public DataAlertViewModelBase(Policy<T> policy, Core.Model.BaseSensorModel sensor)
        {
            EntityId = sensor.Id;
            Id = policy.Id;

            for (int i = 0; i < policy.Conditions.Count; ++i)
            {
                var viewModel = CreateCondition(i == 0);
                var condition = policy.Conditions[i];

                viewModel.Property = condition.Property.ToClient();
                viewModel.Operation = condition.Operation;
                viewModel.Target = condition.Target.Value;

                Conditions.Add(viewModel);
            }

            if (policy.Sensitivity != null)
            {
                var condition = CreateCondition(false);
                var sensitivityViewModel = new TimeIntervalViewModel(null).FromModel(policy.Sensitivity);

                condition.Property = AlertProperty.Sensitivity;
                condition.Sensitivity = new TimeIntervalViewModel(sensitivityViewModel, PredefinedIntervals.ForRestore) { IsAlertBlock = true };

                Conditions.Add(condition);
            }

            Actions.Add(new ActionViewModel(true)
            {
                Action = ActionType.SendNotification,
                Comment = policy.Template,
                DisplayComment = policy.BuildStateAndComment(sensor.LastValue as T, sensor, policy.Conditions[0])
            });

            if (!string.IsNullOrEmpty(policy.Icon))
                Actions.Add(new ActionViewModel(false) { Action = ActionType.ShowIcon, Icon = policy.Icon });

            if (policy.Status == Core.Model.SensorStatus.Error)
                Actions.Add(new ActionViewModel(false) { Action = ActionType.SetStatus });
        }


        protected abstract ConditionViewModel CreateCondition(bool isMain);
    }
}
