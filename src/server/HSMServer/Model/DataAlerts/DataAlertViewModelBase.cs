using HSMServer.Core.Cache.UpdateEntities;
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


        public bool IsDisabled { get; set; }

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

                var target = condition.Property is AlertProperty.Status or AlertProperty.Comment
                    ? new TargetValue(TargetType.LastValue, EntityId.ToString())
                    : new TargetValue(TargetType.Const, condition.Target);

                conditions.Add(new PolicyConditionUpdate(condition.Operation, condition.Property.ToCore(), target));
            }

            (var status, var comment, var icon) = GetActions();

            return new(Id, conditions, sensitivity, status.ToCore(), comment, icon, IsDisabled);
        }

        internal PolicyUpdate ToTimeToLiveUpdate(string initiator)
        {
            (var status, var comment, var icon) = GetActions();

            return new(Id, null, null, status.ToCore(), comment, icon, IsDisabled, initiator);
        }


        private (SensorStatus status, string comment, string icon) GetActions()
        {
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

            return (status, comment, icon);
        }
    }


    public abstract class DataAlertViewModel : DataAlertViewModelBase
    {
        protected virtual string DefaultCommentTemplate { get; } = "[$product]$path $operation $target";

        protected virtual string DefaultIcon { get; }


        protected DataAlertViewModel(Policy policy, NodeViewModel node)
        {
            EntityId = node.Id;
            Id = policy.Id;

            IsDisabled = policy.IsDisabled;

            var availableChats = node.GetAllChats();

            Actions.Add(new ActionViewModel(true, availableChats)
            {
                Action = ActionType.SendNotification,
                Comment = policy.Template,
                DisplayComment = node is SensorNodeViewModel ? policy.RebuildState() : policy.Template
            });

            if (!string.IsNullOrEmpty(policy.Icon))
                Actions.Add(new ActionViewModel(false, availableChats) { Action = ActionType.ShowIcon, Icon = policy.Icon });

            if (policy.Status == Core.Model.SensorStatus.Error)
                Actions.Add(new ActionViewModel(false, availableChats) { Action = ActionType.SetStatus });
        }

        public DataAlertViewModel(NodeViewModel node)
        {
            EntityId = node.Id;
            IsModify = true;

            Conditions.Add(CreateCondition(true));

            var availableChats = node.GetAllChats();

            Actions.Add(new ActionViewModel(true, availableChats) { Comment = DefaultCommentTemplate });
            Actions.Add(new ActionViewModel(false, availableChats) { Action = ActionType.ShowIcon, Icon = DefaultIcon });
        }


        protected abstract ConditionViewModel CreateCondition(bool isMain);
    }


    public class DataAlertViewModel<T> : DataAlertViewModel where T : Core.Model.BaseValue
    {
        public DataAlertViewModel(NodeViewModel node) : base(node) { }

        public DataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor)
            : base(policy, sensor)
        {
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
                var sensitivityViewModel = new TimeIntervalViewModel(null).FromModel(policy.Sensitivity, PredefinedIntervals.ForRestore);

                condition.Property = AlertProperty.Sensitivity;
                condition.Sensitivity = new TimeIntervalViewModel(sensitivityViewModel, PredefinedIntervals.ForRestore) { IsAlertBlock = true };

                Conditions.Add(condition);
            }
        }


        protected override ConditionViewModel CreateCondition(bool isMain) => new ConditionViewModel<T>(isMain);
    }
}
