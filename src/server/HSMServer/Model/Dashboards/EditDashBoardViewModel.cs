using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.Dashboards;

public class EditDashBoardViewModel
{
    public Dictionary<Guid, PanelSettings> Panels { get; set; }


    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public TimeSpan FromPeriod { get; set; }


    public DashboardUpdate ToUpdate() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        FromPeriod = FromPeriod
    };
}