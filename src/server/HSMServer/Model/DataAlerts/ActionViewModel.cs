using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.DataAlerts
{
    public enum ActionType
    {
        SendNotification,
        ShowIcon,
        SetStatus,
    }


    public enum ScheduleRepeatMode
    {
        [Display(Name = "5 minutes")]
        FiveMinutes,
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "15 minutes")]
        FifteenMinutes,
        [Display(Name = "30 minutes")]
        ThirtyMinutes,
        [Display(Name = "Hour")]
        Hourly,
        [Display(Name = "Day")]
        Daily,
        [Display(Name = "Week")]
        Weekly,
    }

    public enum ChatsMode
    {
        [Display(Name = "parent telegram chat(s)")]
        FromParent,
        [Display(Name = "not initialized destination")]
        NotInitialized,
        [Display(Name = "empty destination")]
        Empty,
        [Display(Name = "all chats")]
        All,
        Custom
    }


    public class AlertActionBase
    {
        public ActionType Action { get; set; }


        public ScheduleRepeatMode? ScheduleRepeatMode { get; set; }

        public DateTime? ScheduleStartTime { get; set; }

        public bool ScheduleInstantSend { get; set; }

        public ChatsMode ChatsMode { get; set; }

        public HashSet<Guid> Chats { get; set; } = new();

        public string Comment { get; set; }

        public string Icon { get; set; }


        public string DisplayComment { get; set; }
    }


    public class ActionViewModel : AlertActionBase
    {
        public static readonly string SetErrorStatus = $"set {SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()} status";

        private readonly Dictionary<ActionType, string> _actions = new()
        {
            { ActionType.SendNotification, "send notification" },
            { ActionType.ShowIcon, "show icon" },
            { ActionType.SetStatus, SetErrorStatus },
        };


        public List<SelectListItem> Actions { get; }

        public NodeViewModel Node { get; }

        public bool IsMain { get; }

        public bool IsTtl { get; }


        public ActionViewModel(bool isMain, bool isTtl, NodeViewModel node)
        {
            IsMain = isMain;
            IsTtl = isTtl;
            Actions = _actions.ToSelectedItems(k => k.Value, v => v.Key.ToString());
            Node = node;

            Action = ActionType.SendNotification;
            ScheduleStartTime = DateTime.UtcNow.Ceil(TimeSpan.FromHours(1));
        }


        public bool ChatIsSelected(TelegramChat chat) => Chats?.Contains(chat.Id) ?? false;
    }
}
