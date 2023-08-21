using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Telegram;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public enum ActionType
    {
        SendNotification,
        ShowIcon,
        SetStatus,
    }


    public class AlertActionBase
    {
        public ActionType Action { get; set; }


        public Dictionary<Guid, string> Chats { get; set; }

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


        public List<TelegramChat> AvailableChats { get; }

        public List<SelectListItem> Actions { get; }

        public bool IsMain { get; }


        public ActionViewModel(bool isMain, List<TelegramChat> availableChats)
        {
            IsMain = isMain;
            Actions = _actions.ToSelectedItems(k => k.Value, v => v.Key.ToString());
            AvailableChats = availableChats;

            Action = ActionType.SendNotification;
        }
    }
}
