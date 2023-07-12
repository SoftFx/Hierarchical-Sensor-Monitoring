using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public class AlertAction
    {
        public string Action { get; set; }


        public string Comment { get; set; }

        public string Icon { get; set; }


        public string DisplayComment { get; set; }
    }


    public class ActionViewModel : AlertAction
    {
        private const string DefaultCommentTemplate = "$sensor $operation $target";

        public const string ShowIconAction = "show icon";
        public const string SetStatusAction = "set error status";
        public const string SendNotifyAction = "send notification";

        public static readonly string SetErrorStatus = $"set {SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()} status";


        public List<SelectListItem> Actions { get; } = new()
        {
            new SelectListItem(SendNotifyAction, SendNotifyAction),
            new SelectListItem(ShowIconAction, ShowIconAction),
            new SelectListItem(SetErrorStatus, SetStatusAction),
        };

        public bool IsMain { get; }


        public ActionViewModel(bool isMain)
        {
            IsMain = isMain;

            Comment = DefaultCommentTemplate;
            Action = Actions.FirstOrDefault()?.Value;
        }
    }
}
