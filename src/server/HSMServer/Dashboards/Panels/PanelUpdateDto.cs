using System;

namespace HSMServer.Dashboards;

public record PanelUpdateDto
{
    public Guid Id { get; set; }

    public string Hovermode { get; set; }
    
    public int HoverDistance { get; set; }

    internal PanelUpdate ToUpdate()
    {
        if (Hovermode.Length > 10)
            Hovermode = "false";
        
        return new PanelUpdate(Id)
        {
            Hovermode = Hovermode,
            HoverDistance = HoverDistance
        };
    }
}