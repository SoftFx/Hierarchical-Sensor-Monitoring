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
        [Display(Name = "EMA (Value)")]
        EmaValue,
        Min,
        Max,
        Mean,
        Count,
        FirstValue,
        LastValue,
        [Display(Name = "EMA (Min)")]
        EmaMin,
        [Display(Name = "EMA (Max)")]
        EmaMax,
        [Display(Name = "EMA (Mean)")]
        EmaMean,
        [Display(Name = "EMA (Count)")]
        EmaCount,
        [Display(Name = "Value length")]
        Length,
        [Display(Name = "Size")]
        OriginalSize,
        [Display(Name = "New data")]
        NewSensorData,
        [Display(Name = "Alert confirmation period")]
        ConfirmationPeriod,
        [Display(Name = "Inactivity period")]
        TimeToLive,
    }


    public class ConditionViewModel
    {
        protected virtual List<AlertProperty> Properties { get; }

        public AlertProperty Property { get; set; }


        public TimeIntervalViewModel ConfirmationPeriod { get; set; }

        public TimeIntervalViewModel TimeToLive { get; set; }


        public PolicyOperation? Operation { get; set; }

        public string Target { get; set; }

        public List<SelectListItem> PropertiesItems { get; }

        public bool IsMain { get; }

        public ConditionViewModel() { }

        public ConditionViewModel(bool isMain)
        {
            IsMain = isMain;

            ConfirmationPeriod = new TimeIntervalViewModel(PredefinedIntervals.ForRestore) { IsAlertBlock = true };
            TimeToLive = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };

            PropertiesItems = Properties.ToSelectedItems(k => k.GetDisplayName());

            if (!isMain)
                PropertiesItems.Add(new SelectListItem(AlertProperty.ConfirmationPeriod.GetDisplayName(), nameof(AlertProperty.ConfirmationPeriod)));

            Property = Enum.Parse<AlertProperty>(PropertiesItems.FirstOrDefault()?.Value);
        }
    }
}
