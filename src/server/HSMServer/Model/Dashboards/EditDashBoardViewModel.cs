using System;
using System.Collections.Generic;
using HSMServer.Dashboards;

namespace HSMServer.Model.Dashboards;

public class EditDashBoardViewModel
{
    public Guid Id { get; set; }
    
    public string Name { get; set; }
    
    public string Description { get; set; }
    
    public TimeSpan FromPeriod { get; set; }
    
    public Dictionary<Guid, Cords> Panels { get; set; }
}