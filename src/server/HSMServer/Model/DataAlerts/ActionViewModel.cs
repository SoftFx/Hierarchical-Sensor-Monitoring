using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public class AlertAction
    {
        public SensorStatus Status { get; set; } = SensorStatus.Ok;

        [Required]
        public string Comment { get; set; }

        public string Icon { get; set; }
    }


    public class ActionViewModel : AlertAction
    {
        private const string DefaultCommentTemplate = "$sensor $action $target";

        public const string ShowIconAction = "Show icon";
        public const string SetStatusAction = "Set status";
        public const string SendNotifyAction = "Send notification";


        public List<SelectListItem> StatusesItems { get; }

        public bool IsMain { get; }


        [Required]
        public string Comment { get; set; }


        public ActionViewModel(bool isMain)
        {
            IsMain = isMain;
            StatusesItems = AlertPredefined.Statuses.Select(s => new SelectListItem(s.Value, $"{s.Key}")).ToList();

            Comment = DefaultCommentTemplate;
        }
    }
}
