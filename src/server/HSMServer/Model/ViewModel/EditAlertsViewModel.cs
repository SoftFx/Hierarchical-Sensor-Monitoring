using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Attributes;

namespace HSMServer.Model.ViewModel;

public class EditAlertsViewModel
{
    public List<Guid> SelectedNodes => string.IsNullOrEmpty(NodeIds) ? new () : NodeIds.Split(',').Select(Guid.Parse).ToList();

    
    [Display(Name = "Time to live interval")]
    [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
    public TimeIntervalViewModel ExpectedUpdateInterval { get; set; } = new (PredefinedIntervals.ForTimeout);

    [Display(Name = "Sensitivity interval")]
    [MinTimeInterval(TimeInterval.OneMinute, ErrorMessage = "{0} minimal value is {1}.")]
    public TimeIntervalViewModel SensorRestorePolicy { get; set; } = new (PredefinedIntervals.ForRestore);
    
    public string NodeIds { get; set; }
}