using HSMServer.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Extensions;

namespace HSMServer.Model.ViewModel;

public sealed class EditAlertsViewModel
{
    public List<Guid> SelectedNodes => string.IsNullOrEmpty(NodeIds) ? new() : NodeIds.Split(',').Select(x => x.ToGuid()).ToList();


    [Display(Name = "Time to live interval")]
    [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
    public TimeIntervalViewModel ExpectedUpdateInterval { get; set; } = new(PredefinedIntervals.ForTimeout);

    [Display(Name = "Sensitivity interval")]
    [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
    public TimeIntervalViewModel SensorRestorePolicy { get; set; } = new(PredefinedIntervals.ForRestore);

    public string NodeIds { get; set; }


    public void Upload()
    {
        if (!ExpectedUpdateInterval.Interval.HasValue)
            ExpectedUpdateInterval = null;

        if (!SensorRestorePolicy.Interval.HasValue)
            SensorRestorePolicy = null;
    }
}