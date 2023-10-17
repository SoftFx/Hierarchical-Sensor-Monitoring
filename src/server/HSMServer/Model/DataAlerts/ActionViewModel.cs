using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
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


        public HashSet<Guid> Chats { get; set; } = new();

        public string Comment { get; set; }

        public string Icon { get; set; }


        public string DisplayComment { get; set; }
    }


    public class ActionViewModel : AlertActionBase
    {
        public static readonly Guid AllChatsId = Guid.Empty;
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


        public ActionViewModel(bool isMain, NodeViewModel node)
        {
            IsMain = isMain;
            Actions = _actions.ToSelectedItems(k => k.Value, v => v.Key.ToString());
            Node = node;

            Action = ActionType.SendNotification;
        }


        public bool ChatIsSelected(TelegramChat chat) => Chats?.Contains(chat.Id) ?? false;
    }
}
