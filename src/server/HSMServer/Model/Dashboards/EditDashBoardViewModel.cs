using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Dashboards;

namespace HSMServer.Model.Dashboards;

public class EditDashBoardViewModel
{
    public Guid Id { get; set; }
    
    public string Name { get; set; }
    
    public string Description { get; set; }
    
    public TimeSpan FromPeriod { get; set; }
    
    public Dictionary<Guid, PanelSettingsEntity> Panels { get; set; }

    public DashboardUpdate ToUpdate() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        FromPeriod = FromPeriod
    };
}