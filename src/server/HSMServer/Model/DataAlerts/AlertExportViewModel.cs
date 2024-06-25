using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public sealed class AlertExportViewModel
    {
        private const string NotInitializedChat = "#notinitialized";
        private const string FromParentChat = "#fromparent";
        private const string EmptyChat = "#empty";
        private const string AllChats = "#all";

        private readonly Dictionary<PolicyDestinationMode, string> _chatsModeToKeyWords = new()
        {
            { PolicyDestinationMode.NotInitialized, NotInitializedChat },
            { PolicyDestinationMode.FromParent, FromParentChat },
            { PolicyDestinationMode.AllChats, AllChats },
            { PolicyDestinationMode.Empty, EmptyChat },
        };


        public List<string> Products { get; set; }

        public List<string> Sensors { get; set; }

        public List<ConditionExportViewModel> Conditions { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }

        public SensorStatus Status { get; set; }

        public TimeSpan? ConfirmationPeriod { get; set; }

        public string ScheduledNotificationTime { get; set; }

        public AlertRepeatMode? ScheduledRepeatMode { get; set; }

        public bool ScheduledInstantSend { get; set; }

        public List<string> Chats { get; set; } = [];

        public bool IsDisabled { get; set; }


        public AlertExportViewModel() { }

        internal AlertExportViewModel(IEnumerable<PolicyExportInfo> infoList, Dictionary<Guid, string> availableChats)
        {
            Sensors = infoList.Select(u => u.FullRelativePath).OrderBy(u => u).ToList();

            var info = infoList.First();
            var policy = info.Policy;

            if (info.ProductName is not null)
                Products = new List<string>() { info.ProductName };

            Icon = policy.Icon;
            Status = policy.Status;
            Template = policy.Template;
            ConfirmationPeriod = policy.ConfirmationPeriod.HasValue ? new TimeSpan(policy.ConfirmationPeriod.Value) : null;
            IsDisabled = policy.IsDisabled;
            ScheduledNotificationTime = policy.Schedule.Time == DateTime.MinValue ? null : policy.Schedule.Time.ToDefaultFormat();
            ScheduledRepeatMode = policy.Schedule.RepeatMode; // TODO: null if None or Immediatly?
            ScheduledInstantSend = policy.Schedule.InstantSend;

            if (_chatsModeToKeyWords.TryGetValue(policy.Destination.Mode, out var keyWord))
                Chats.Add(keyWord);
            else
                foreach (var (id, _) in policy.Destination.Chats)
                    if (availableChats.TryGetValue(id, out var name))
                        Chats.Add(name);

            if (policy.Destination.Mode is PolicyDestinationMode.FromParent)
            {
                foreach (var (id, _) in policy.Destination.Chats)
                    if (availableChats.TryGetValue(id, out var name))
                        Chats.Add(name);
            }

            Conditions = policy.Conditions.Select(c => new ConditionExportViewModel(c)).ToList();
        }

        internal PolicyUpdate ToUpdate(Guid sensorId, Dictionary<string, Guid> availableChats)
        {
            PolicyDestinationMode? mode = PolicyDestinationMode.Custom;
            Dictionary<Guid, string> chats = [];

            if (Chats is not null)
            {
                var keyWordsToChatsMode = _chatsModeToKeyWords.ToDictionary(u => u.Value, u => u.Key);
                foreach (var chat in Chats)
                {
                    if (keyWordsToChatsMode.TryGetValue(chat, out var chatMode))
                    {
                        mode = chatMode;

                        if (chatMode != PolicyDestinationMode.FromParent)
                        {
                            chats = [];
                            break;
                        }
                    }

                    if (availableChats.TryGetValue(chat, out var chatId))
                        chats.Add(chatId, chat);
                }
            }
            else
            {
                mode = null;
                chats = null;
            }

            return new()
            {
                Icon = Icon,
                Status = Status,
                Template = Template,
                IsDisabled = IsDisabled,
                ConfirmationPeriod = ConfirmationPeriod?.Ticks,
                Conditions = Conditions.Select(c => c.ToUpdate(sensorId)).ToList(),
                Schedule = new PolicyScheduleUpdate()
                {
                    Time = ScheduledNotificationTime.ParseFromDefault(),
                    RepeatMode = ScheduledRepeatMode,
                    InstantSend = ScheduledInstantSend,
                },
                Destination = new PolicyDestinationUpdate(chats, mode),
            };
        }
    }


    public sealed class ConditionExportViewModel
    {
        public PolicyProperty Property { get; set; }

        public PolicyOperation Operation { get; set; }

        public string Target { get; set; }


        public ConditionExportViewModel() { }

        internal ConditionExportViewModel(PolicyCondition condition)
        {
            Property = condition.Property;
            Operation = condition.Operation;
            Target = condition.Target.Type == TargetType.Const ? condition.Target.Value : null;
        }


        internal PolicyConditionUpdate ToUpdate(Guid sensorId) =>
            new(Operation,
                Property,
                Operation.IsTargetVisible()
                    ? new(TargetType.Const, Target)
                    : new(TargetType.LastValue, sensorId.ToString()));
    }
}
