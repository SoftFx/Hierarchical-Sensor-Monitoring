﻿using HSMCommon.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.Controls;
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


        public DefaultChatViewModel ParentDefaultChat { get; protected set; }
        
        public DefaultChatViewModel DefaultChat { get; protected set; }

        public bool IsModify { get; protected set; }

        public virtual bool IsTtl { get; } = false;


        internal bool IsAlertDisplayed
        {
            get
            {
                var firstConfition = Conditions.FirstOrDefault();

                return firstConfition?.Property != AlertProperty.TimeToLive || firstConfition?.TimeToLive.DisplayValue != TimeInterval.None.GetDisplayName();
            }
        }


        internal PolicyUpdate ToUpdate(Dictionary<Guid, string> availavleChats)
        {
            List<PolicyConditionUpdate> conditions = new(Conditions.Count);
            Core.Model.TimeIntervalModel confirmationPeriod = null;

            foreach (var condition in Conditions)
            {
                if (condition.Property == AlertProperty.TimeToLive)
                    continue;

                if (condition.Property == AlertProperty.ConfirmationPeriod)
                {
                    confirmationPeriod = condition.ConfirmationPeriod.ToModel();
                    continue;
                }

                var target = condition.Operation.Value.IsTargetVisible()
                    ? new TargetValue(TargetType.Const, condition.Target)
                    : new TargetValue(TargetType.LastValue, EntityId.ToString());

                conditions.Add(new PolicyConditionUpdate(condition.Operation.Value, condition.Property.ToCore(), target));
            }

            var actions = GetActions(availavleChats);

            return new()
            {
                Id = Id,
                Conditions = conditions,
                ConfirmationPeriod = confirmationPeriod?.Ticks,
                Status = actions.Status.ToCore(),
                Template = actions.Comment,
                Icon = actions.Icon,
                IsDisabled = IsDisabled,
                Schedule = actions.Schedule,
                Destination = actions.Destination,
            };
        }

        internal PolicyUpdate ToTimeToLiveUpdate(InitiatorInfo initiator, Dictionary<Guid, string> availavleChats)
        {
            var actions = GetActions(availavleChats);

            return new()
            {
                Id = Id,
                Status = actions.Status.ToCore(),
                Template = actions.Comment,
                Icon = actions.Icon,
                IsDisabled = IsDisabled,
                Destination = actions.Destination,
                Schedule = actions.Schedule,
                Initiator = initiator,
            };
        }


        private ActionProperties GetActions(Dictionary<Guid, string> availavleChats)
        {
            PolicyDestinationUpdate destination = new();
            SensorStatus status = SensorStatus.Ok;
            PolicyScheduleUpdate schedule = null;
            string comment = null;
            string icon = null;

            foreach (var action in Actions)
            {
                if (action.Action == ActionType.SendNotification)
                {
                    schedule = new PolicyScheduleUpdate()
                    {
                        Time = action.ScheduleStartTime.ToCoreScheduleTime(),
                        RepeatMode = action.ScheduleRepeatMode.ToCore(),
                        InstantSend = action.ScheduleInstantSend
                    };

                    destination = new PolicyDestinationUpdate(action.Chats?.ToDictionary(k => k, v => availavleChats[v]) ?? new(0), action.ChatsMode.ToCore());
                    comment = action.Comment;
                }
                else if (action.Action == ActionType.ShowIcon)
                    icon = action.Icon;
                else if (action.Action == ActionType.SetStatus)
                    status = SensorStatus.Error;
            }

            return new(comment, destination, schedule, status, icon);
        }


        private record ActionProperties(
            string Comment,
            PolicyDestinationUpdate Destination,
            PolicyScheduleUpdate Schedule,
            SensorStatus Status,
            string Icon);
    }


    public abstract class DataAlertViewModel : DataAlertViewModelBase
    {
        private bool IsActionMain => Actions.Count == 0;


        protected virtual string DefaultCommentTemplate { get; } = "[$product]$path $operation $target";

        protected virtual string DefaultIcon { get; }


        protected DataAlertViewModel(Policy policy, NodeViewModel node)
        {
            InitializeDefaultChat(node);
            EntityId = node.Id;
            Id = policy.Id;

            IsDisabled = policy.IsDisabled;

            if (!string.IsNullOrEmpty(policy.Template))
            {
                var action = new ActionViewModel(IsActionMain, IsTtl, node)
                {
                    Action = ActionType.SendNotification,
                    Comment = policy.Template,
                    DisplayComment = node is SensorNodeViewModel ? policy.RebuildState() : policy.Template,
                    ScheduleStartTime = policy.Schedule.Time.ToClientScheduleTime(),
                    ScheduleRepeatMode = policy.Schedule.RepeatMode.ToClient(),
                    ScheduleInstantSend = policy.Schedule.InstantSend,
                    ChatsMode = policy.Destination.Mode.ToClient(),
                };

                if (policy.Destination.IsCustom || policy.Destination.IsFromParentChats)
                    foreach (var chat in policy.Destination.Chats)
                        action.Chats.Add(chat.Key);

                Actions.Add(action);
            }

            if (!string.IsNullOrEmpty(policy.Icon))
                Actions.Add(new ActionViewModel(IsActionMain, IsTtl, node) { Action = ActionType.ShowIcon, Icon = policy.Icon });

            if (policy.Status == Core.Model.SensorStatus.Error)
                Actions.Add(new ActionViewModel(IsActionMain, IsTtl, node) { Action = ActionType.SetStatus });
        }

        public DataAlertViewModel(NodeViewModel node)
        {
            InitializeDefaultChat(node);
            EntityId = node.Id;
            IsModify = true;

            Conditions.Add(CreateCondition(true));

            Actions.Add(new ActionViewModel(true, IsTtl, node) { Comment = DefaultCommentTemplate });
            Actions.Add(new ActionViewModel(false, IsTtl, node) { Action = ActionType.ShowIcon, Icon = DefaultIcon });
        }


        protected abstract ConditionViewModel CreateCondition(bool isMain);

        private void InitializeDefaultChat(NodeViewModel node)
        {
            ParentDefaultChat = node.Parent?.DefaultChats;
            DefaultChat = node.DefaultChats;
        }
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

            if (policy.ConfirmationPeriod != null)
            {
                var condition = CreateCondition(false);

                condition.Property = AlertProperty.ConfirmationPeriod;
                condition.ConfirmationPeriod = new TimeIntervalViewModel(PredefinedIntervals.ForRestore)
                {
                    IsAlertBlock = true,
                };

                if (policy.ConfirmationPeriod.HasValue)
                    condition.ConfirmationPeriod.FromModel(new Core.Model.TimeIntervalModel(policy.ConfirmationPeriod.Value), PredefinedIntervals.ForRestore);

                Conditions.Add(condition);
            }
        }


        protected override ConditionViewModel CreateCondition(bool isMain) => new CommonConditionViewModel(isMain);
    }
}
