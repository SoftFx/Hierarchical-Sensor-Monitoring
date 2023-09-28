using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.DataAlerts
{
    public enum AlertProperty
    {
        Status,
        Comment,
        Value,
        Min,
        Max,
        Mean,
        Count,
        LastValue,
        [Display(Name = "Value length")]
        Length,
        [Display(Name = "Size")]
        OriginalSize,
        [Display(Name = "New data")]
        NewSensorData,
        Sensitivity,
        [Display(Name = "Inactivity period")]
        TimeToLive,
    }


    public class AlertConditionBase
    {
        public AlertProperty Property { get; set; }


        public TimeIntervalViewModel Sensitivity { get; set; }

        public TimeIntervalViewModel TimeToLive { get; set; }


        public PolicyOperation? Operation { get; set; }

        public string Target { get; set; }
    }


    public abstract class ConditionViewModel : AlertConditionBase
    {
        protected abstract List<AlertProperty> Properties { get; }


        public bool IsMain { get; }

        public List<SelectListItem> PropertiesItems { get; }


        public ConditionViewModel(bool isMain)
        {
            IsMain = isMain;

            Sensitivity = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };
            TimeToLive = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };

            PropertiesItems = Properties.ToSelectedItems(k => k.GetDisplayName());

            //if (!isMain)
            //    PropertiesItems.Add(new SelectListItem(AlertProperty.Sensitivity.GetDisplayName(), nameof(AlertProperty.Sensitivity)));

            Property = Enum.Parse<AlertProperty>(PropertiesItems.FirstOrDefault()?.Value);
        }
    }
}
