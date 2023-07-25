using System;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Model.DataAlerts;

public class AlertMessageViewModel
{
    public AlertProperty Property { get; set; }

    public PolicyOperation Operation { get; set; }

    public string Emoji { get; set; }
    
    public string Comment { get; set; }
    
    public string Target { get; set; }
    
    public Guid EntityId { get; set; }
}