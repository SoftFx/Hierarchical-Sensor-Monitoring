using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public class AlertAction
    {
        public string Action { get; set; }


        public SensorStatus Status { get; set; }

        public string Comment { get; set; }

        public string Icon { get; set; }


        public string DisplayComment { get; set; }
    }


    public class ActionViewModel : AlertAction
    {
        private const string DefaultCommentTemplate = "$sensor $operation $target";

        public const string ShowIconAction = "show icon";
        public const string SetStatusAction = "set sensor status";
        public const string SendNotifyAction = "send notification";


        public List<SelectListItem> Actions { get; } = new()
        {
            new SelectListItem(SendNotifyAction, SendNotifyAction),
            new SelectListItem(ShowIconAction, ShowIconAction),
            new SelectListItem(SetStatusAction, SetStatusAction),
        };

        public List<SelectListItem> StatusesItems { get; }

        public bool IsMain { get; }


        public ActionViewModel(bool isMain)
        {
            IsMain = isMain;
            StatusesItems = AlertPredefined.Statuses.Select(s => new SelectListItem(s.Value, $"{s.Key}")).ToList();

            Comment = DefaultCommentTemplate;
            Action = Actions.FirstOrDefault()?.Value;
        }
    }
}
