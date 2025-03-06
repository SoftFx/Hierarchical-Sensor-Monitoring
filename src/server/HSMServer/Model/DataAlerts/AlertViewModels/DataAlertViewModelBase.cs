using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMCommon.Extensions;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.Controls;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Html;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

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

        public bool IsModify { get; set; }

        public virtual SensorType Type { get; }

        public virtual bool IsTtl { get; } = false;

        public bool IsTemplate { get; protected set; }

        public Guid? TemplateId { get; set; }

        internal bool IsAlertDisplayed
        {
            get
            {
                var firstConfition = Conditions.FirstOrDefault();

                return firstConfition?.Property != AlertProperty.TimeToLive || firstConfition?.TimeToLive.DisplayValue != TimeInterval.None.GetDisplayName();
            }
        }

        internal PolicyUpdate ToUpdate(Dictionary<Guid, string> availableChats)
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

                if (condition.Operation != null)
                {
                    var target = condition.Operation.Value.IsTargetVisible()
                        ? new TargetValue(TargetType.Const, condition.Target)
                        : new TargetValue(TargetType.LastValue, EntityId.ToString());

                    conditions.Add(new PolicyConditionUpdate(condition.Operation.Value, condition.Property.ToCore(), target));
                }
            }

            var actions = GetActions(availableChats);

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
                TemplateId = TemplateId
            };
        }

        internal PolicyUpdate ToTimeToLiveUpdate(InitiatorInfo initiator, Dictionary<Guid, string> availableChats)
        {
            var actions = GetActions(availableChats);

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


        private ActionProperties GetActions(Dictionary<Guid, string> availableChats)
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

                    destination = new PolicyDestinationUpdate(action.Chats?.ToDictionary(k => k, v => availableChats[v]) ?? new(0), action.ChatsMode.ToCore());
                    comment = action.Comment;
                }
                else if (action.Action == ActionType.ShowIcon)
                    icon = action.Icon;
                else if (action.Action == ActionType.SetStatus)
                    status = SensorStatus.Error;
            }

            return new(comment, destination, schedule, status, icon);
        }


        public string ToString(ITelegramChatsManager manager)
        {
            var telegramChats = manager.GetValues().ToDictionary(k => k.Id, v => v);

            string GetActionChats(AlertActionBase action) =>
                 string.Join(", ", action.Chats.Where(ch => telegramChats.ContainsKey(ch)).Select(ch => telegramChats[ch].Name));

            var sb = new StringBuilder(128);
            sb.Append("If ");

            for (int i = 0; i < Conditions.Count; ++i)
            {
                var condition = Conditions[i];

                if (i > 0)
                    sb.Append(" and ");


                if (condition.Property == AlertProperty.TimeToLive)
                {
                    sb.Append(AlertProperty.TimeToLive.GetDisplayName());
                    sb.Append(" is ");
                    sb.Append(condition.TimeToLive.DisplayValue);
                }
                else if (condition.Property == AlertProperty.ConfirmationPeriod)
                {
                    sb.Append(AlertProperty.ConfirmationPeriod.GetDisplayName());
                    sb.Append(" is more than ");
                    sb.Append(condition.ConfirmationPeriod.DisplayValue);
                }
                else
                {
                    sb.Append(condition.Property.GetDisplayName());
                    sb.Append(condition.Operation?.GetDisplayName());
                    if (condition.Operation?.IsTargetVisible() ?? false)
                    {
                        sb.Append(condition.Target);
                    }
                }
            }

            var actions = new List<HtmlString>();
            for (int i = 0; i < Actions.Count; ++i)
            {
                var action = Actions[i];

                if (i > 0)
                {
                    sb.Append(" and ");
                }

                if (action.Action == ActionType.SendNotification)
                {
                    string chats = null;

                    switch (action.ChatsMode)
                    {
                        case ChatsMode.FromParent:
                            {
                                var (parentIds, lastMode) = DefaultChat?.GetParentChats() ?? ([], null);

                                string chatNames;
                                if (parentIds.Count != 0)
                                    chatNames = parentIds.ToNames(telegramChats);
                                else
                                    chatNames = lastMode?.GetDisplayName();

                                chats = $"{ChatsMode.FromParent.GetDisplayName()} {(string.IsNullOrEmpty(chatNames) ? "" : $"({chatNames})")}";

                                if (action.Chats.Count != 0)
                                    chats += $", {GetActionChats(action)}";

                                break;
                            }
                        case ChatsMode.Custom:
                            {
                                if (action.Chats.Count == 0)
                                {
                                    chats = "(chats are not initialized)";
                                }
                                else
                                    chats += GetActionChats(action);

                                break;
                            }
                        default:
                            chats = action.ChatsMode.GetDisplayName();
                            break;
                    }

                    sb.Append(" send notification ");
                    sb.Append(action.DisplayComment);
                    sb.Append(" to ");
                    sb.Append(chats);

                    if (action.ScheduleRepeatMode is not null)
                    {
                        sb.Append($" scheduled every ");
                        sb.Append(action.ScheduleRepeatMode.GetDisplayName());
                        if (!Conditions.Any(c => c.Property == AlertProperty.TimeToLive))
                        {
                            if (action.ScheduleStartTime.HasValue && action.ScheduleStartTime.Value > DateTime.UtcNow)
                            {
                                sb.Append($" starting at ");
                                sb.Append(action.ScheduleStartTime?.ToDefaultFormat());
                            }

                            if (action.ScheduleInstantSend)
                                sb.Append(" and instant send");
                        }
                    }
                }
                else if (action.Action == ActionType.ShowIcon)
                {
                    sb.Append(" show icon ");
                    sb.Append(action.Icon);
                }
                else if (action.Action == ActionType.SetStatus)
                {
                    sb.Append(ActionViewModel.SetErrorStatus);
                }
            }

            if (Actions.Count == 0)
                sb.Append(" add any action");

            return sb.ToString();
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

        protected DataAlertViewModel() 
        {
            ParentDefaultChat = new DefaultChatViewModel();
            DefaultChat = new DefaultChatViewModel();

            IsModify = true;
            IsTemplate = true;

            Conditions.Add(CreateCondition(true));

            Actions.Add(new ActionViewModel(true, IsTtl) { Comment = DefaultCommentTemplate });
            Actions.Add(new ActionViewModel(false, IsTtl) { Action = ActionType.ShowIcon, Icon = DefaultIcon });
        }

        protected DataAlertViewModel(Policy policy, NodeViewModel node)
        {
            InitializeDefaultChat(node);
            EntityId = node?.Id ?? new Guid();
            Id = policy.Id;
            TemplateId = policy.TemplateId;

            IsDisabled = policy.IsDisabled;

            HashSet<Guid> chats = [];
            node?.TryGetChats(out chats);

            if (!string.IsNullOrEmpty(policy.Template))
            {
                var action = new ActionViewModel(IsActionMain, IsTtl, chats)
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
                Actions.Add(new ActionViewModel(IsActionMain, IsTtl, chats) { Action = ActionType.ShowIcon, Icon = policy.Icon });

            if (policy.Status == Core.Model.SensorStatus.Error)
                Actions.Add(new ActionViewModel(IsActionMain, IsTtl, chats) { Action = ActionType.SetStatus });
        }

        public DataAlertViewModel(NodeViewModel node)
        {
            InitializeDefaultChat(node);
            EntityId = node?.Id ?? new Guid();
            Id = new Guid();
            IsModify = true;

            Conditions.Add(CreateCondition(true));

            HashSet<Guid> chats = [];
            node?.TryGetChats(out chats);

            Actions.Add(new ActionViewModel(true, IsTtl, chats) { Comment = DefaultCommentTemplate });
            Actions.Add(new ActionViewModel(false, IsTtl, chats) { Action = ActionType.ShowIcon, Icon = DefaultIcon });
        }

        public static DataAlertViewModelBase BuildAlert(Policy policy, SensorNodeViewModel model) => policy switch
        {
            FilePolicy p => new FileDataAlertViewModel(p, model),
            StringPolicy p => new StringDataAlertViewModel(p, model),
            BooleanPolicy p => new DataAlertViewModel<BooleanValue>(p, model),
            VersionPolicy p => new SingleDataAlertViewModel<VersionValue>(p, model),
            TimeSpanPolicy p => new SingleDataAlertViewModel<TimeSpanValue>(p, model),
            IntegerPolicy p => new NumericDataAlertViewModel<IntegerValue>(p, model),
            DoublePolicy p => new NumericDataAlertViewModel<DoubleValue>(p, model),
            RatePolicy p => new NumericDataAlertViewModel<RateValue>(p, model),
            IntegerBarPolicy p => new BarDataAlertViewModel<IntegerBarValue>(p, model),
            DoubleBarPolicy p => new BarDataAlertViewModel<DoubleBarValue>(p, model),
            EnumPolicy p => new NumericDataAlertViewModel<EnumValue>(p, model),
            TTLPolicy p => new TimeToLiveAlertViewModel(p, model),
            _ => null
        };

        public static DataAlertViewModelBase BuildAlert(Policy policy) => BuildAlert(policy, null);

        public static DataAlertViewModelBase BuildAlert(byte type, NodeViewModel node) => type switch
        {
            (byte)SensorType.File => new FileDataAlertViewModel(node),
            (byte)SensorType.String => new StringDataAlertViewModel(node),
            (byte)SensorType.Boolean => new BooleanDataAlertViewModel(node),
            (byte)SensorType.Version => new VersionDataAlertViewModel(node),
            (byte)SensorType.TimeSpan => new TimeSpanDataAlertViewModel(node),
            (byte)SensorType.Integer => new IntegerDataAlertViewModel(node),
            (byte)SensorType.Double => new DoubleDataAlertViewModel(node),
            (byte)SensorType.Rate => new RateDataAlertViewModel(node),
            (byte)SensorType.IntegerBar => new IntegerBarDataAlertViewModel(node),
            (byte)SensorType.DoubleBar => new DoubleBarDataAlertViewModel(node),
            (byte)SensorType.Enum => new EnumDataAlertViewModel(node),
            TimeToLiveAlertViewModel.AlertKey => new TimeToLiveAlertViewModel(node),
            _ => null,
        };

        public static DataAlertViewModelBase BuildAlert(byte type) => type switch
        {
            (byte)SensorType.File => new FileDataAlertViewModel(),
            (byte)SensorType.String => new StringDataAlertViewModel(),
            (byte)SensorType.Boolean => new BooleanDataAlertViewModel(),
            (byte)SensorType.Version => new VersionDataAlertViewModel(),
            (byte)SensorType.TimeSpan => new TimeSpanDataAlertViewModel(),
            (byte)SensorType.Integer => new IntegerDataAlertViewModel(),
            (byte)SensorType.Double => new DoubleDataAlertViewModel(),
            (byte)SensorType.Rate => new RateDataAlertViewModel(),
            (byte)SensorType.IntegerBar => new IntegerBarDataAlertViewModel(),
            (byte)SensorType.DoubleBar => new DoubleBarDataAlertViewModel(),
            (byte)SensorType.Enum => new EnumDataAlertViewModel(),
            TimeToLiveAlertViewModel.AlertKey => new TimeToLiveAlertViewModel(),
            _ => null,
        };


        protected abstract ConditionViewModel CreateCondition(bool isMain);

        private void InitializeDefaultChat(NodeViewModel node)
        {
            ParentDefaultChat = node?.Parent?.DefaultChats ?? new DefaultChatViewModel();
            DefaultChat = node?.DefaultChats ?? new DefaultChatViewModel();
        }
    }


    public class DataAlertViewModel<T> : DataAlertViewModel where T : Core.Model.BaseValue
    {
        public DataAlertViewModel() : base() { }

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
