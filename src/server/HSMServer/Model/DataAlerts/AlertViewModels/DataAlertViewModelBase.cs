using HSMCommon.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

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


        internal bool IsAlertDisplayed
        {
            get
            {
                var firstConfition = Conditions.FirstOrDefault();

                if (firstConfition?.Property == AlertProperty.TimeToLive)
                {
                    var displayValue = firstConfition.TimeToLive.DisplayValue;
                    var neverInterval = TimeInterval.None.GetDisplayName();
                    var isTtlNever = displayValue == TimeInterval.FromParent.ToFromParentDisplay(neverInterval) || displayValue == neverInterval;

                    return !isTtlNever;
                }

                return true;
            }
        }


        internal PolicyUpdate ToUpdate(Dictionary<Guid, string> availavleChats)
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

                var target = condition.Operation.Value.IsTargetVisible()
                    ? new TargetValue(TargetType.Const, condition.Target)
                    : new TargetValue(TargetType.LastValue, EntityId.ToString());

                conditions.Add(new PolicyConditionUpdate(condition.Operation.Value, condition.Property.ToCore(), target));
            }

            (var status, var destination, var comment, var icon) = GetActions(availavleChats);

            return new()
            {
                Id = Id,
                Conditions = conditions,
                Sensitivity = sensitivity,
                Status = status.ToCore(),
                Template = comment,
                Icon = icon,
                IsDisabled = IsDisabled,
                Destination = destination,
            };
        }

        internal PolicyUpdate ToTimeToLiveUpdate(InitiatorInfo initiator, Dictionary<Guid, string> availavleChats)
        {
            (var status, var destination, var comment, var icon) = GetActions(availavleChats);

            return new()
            {
                Id = Id,
                Status = status.ToCore(),
                Template = comment,
                Icon = icon,
                IsDisabled = IsDisabled,
                Destination = destination,
                Initiator = initiator,
            };
        }


        private (SensorStatus status, PolicyDestinationUpdate destination, string comment, string icon) GetActions(Dictionary<Guid, string> availavleChats)
        {
            PolicyDestinationUpdate destination = null;
            SensorStatus status = SensorStatus.Ok;
            string comment = null;
            string icon = null;

            foreach (var action in Actions)
            {
                if (action.Action == ActionType.SendNotification)
                {
                    bool allChats = action.Chats?.Contains(ActionViewModel.AllChatsId) ?? false;
                    Dictionary<Guid, string> chats = allChats
                        ? new(0)
                        : action.Chats?.ToDictionary(k => k, v => availavleChats[v]) ?? new(0);

                    destination = new PolicyDestinationUpdate(chats, allChats);
                    comment = action.Comment;
                }
                else if (action.Action == ActionType.ShowIcon)
                    icon = action.Icon;
                else if (action.Action == ActionType.SetStatus)
                    status = SensorStatus.Error;
            }

            return (status, destination, comment, icon);
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

            node.TryGetChats(out var availableChats);

            Actions.Add(new ActionViewModel(true, availableChats)
            {
                Action = ActionType.SendNotification,
                Comment = policy.Template,
                DisplayComment = node is SensorNodeViewModel ? policy.RebuildState() : policy.Template,
                Chats = policy.Destination.AllChats
                    ? new HashSet<Guid>() { ActionViewModel.AllChatsId }
                    : new HashSet<Guid>(policy.Destination.Chats.Keys),
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

            node.TryGetChats(out var availableChats);

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

                condition.Property = AlertProperty.Sensitivity;
                condition.Sensitivity = new TimeIntervalViewModel(PredefinedIntervals.ForRestore)
                {
                    IsAlertBlock = true,
                }.FromModel(policy.Sensitivity, PredefinedIntervals.ForRestore);

                Conditions.Add(condition);
            }
        }


        protected override ConditionViewModel CreateCondition(bool isMain) => new CommonConditionViewModel(isMain);
    }
}
