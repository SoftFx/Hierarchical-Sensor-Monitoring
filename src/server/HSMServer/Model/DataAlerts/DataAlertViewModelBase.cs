using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class DataAlertViewModel
    {
        public List<AlertCondition> Conditions { get; set; } = new();

        public List<AlertAction> Actions { get; set; } = new();


        public Guid EntityId { get; set; }

        public Guid Id { get; set; }


        internal DataPolicyUpdate ToUpdate()
        {
            List<PolicyConditionUpdate> conditions = new(Conditions.Count);
            Core.Model.TimeIntervalModel sensitivity = null;

            foreach (var condition in Conditions)
            {
                if (condition.Property == ConditionViewModel.SensitivityCondition)
                {
                    sensitivity = condition.Sensitivity.ToModel();
                    continue;
                }

                if (condition.Property != ConditionViewModel.TimeToLiveCondition)
                    conditions.Add(new PolicyConditionUpdate(condition.Operation, new TargetValue(TargetType.Const, condition.Target), condition.Property));
            }

            SensorStatus status = SensorStatus.Ok;
            string comment = null;
            string icon = null;

            foreach (var action in Actions)
            {
                if (action.Action == ActionViewModel.SendNotifyAction)
                    comment = action.Comment;
                else if (action.Action == ActionViewModel.ShowIconAction)
                    icon = action.Icon;
                else if (action.Action == ActionViewModel.SetStatusAction)
                    status = action.Status;
            }


            return new(Id, conditions, sensitivity, status.ToCore(), comment, icon);
        }
    }


    public abstract class DataAlertViewModelBase : DataAlertViewModel
    {
        public string DisplayComment { get; protected set; }

        public bool IsModify { get; protected set; }
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

            Actions.Add(new ActionViewModel(true) { Action = ActionViewModel.SendNotifyAction, Comment = policy.Template });
            if (!string.IsNullOrEmpty(policy.Icon))
                Actions.Add(new ActionViewModel(false) { Action = ActionViewModel.ShowIconAction, Icon = policy.Icon });
            if (policy.Status != Core.Model.SensorStatus.Ok)
                Actions.Add(new ActionViewModel(false) { Action = ActionViewModel.SetStatusAction, Status = policy.Status.ToClient() });

            for (int i = 0; i < policy.Conditions.Count; ++i)
            {
                var viewModel = CreateCondition(i == 0);

                viewModel.Property = policy.Conditions[i].Property;
                viewModel.Operation = policy.Conditions[i].Operation;
                viewModel.Target = policy.Conditions[i].Target.Value;

                Conditions.Add(viewModel);

                //    DisplayComment = policy.BuildStateAndComment(sensor.LastValue as T, sensor, policy.Conditions[0]);
            }
        }


        protected abstract ConditionViewModel CreateCondition(bool isFirst);
    }
}
