using HSMServer.Attributes;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.ViewModel;

public sealed class EditAlertsViewModel
{
    public List<Guid> SelectedNodes => string.IsNullOrEmpty(NodeIds) ? new() : NodeIds.Split(',').Select(x => x.ToGuid()).ToList();


    [Display(Name = "Time to live interval")]
    [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
    public TimeIntervalViewModel ExpectedUpdateInterval { get; set; } = new(PredefinedIntervals.ForTimeout);

    public string NodeIds { get; set; }


    public void Upload()
    {
        if (!ExpectedUpdateInterval.Interval.HasValue)
            ExpectedUpdateInterval = null;
    }
}