using System;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Dashboards;

public sealed record PanelTooltipUpdateDto
{
    public Guid Id { get; set; }
    
    public TooltipHovermode Hovermode { get; set; }

    internal PanelUpdate ToUpdate() =>
        new(Id)
        {
            Hovermode = Hovermode
        };
}