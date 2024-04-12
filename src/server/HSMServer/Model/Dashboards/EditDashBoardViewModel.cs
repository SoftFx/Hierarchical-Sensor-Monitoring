using HSMServer.Dashboards;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.Dashboards;


public sealed class PanelSettingsViewModel
{
    public double Width { get; set; }

    public double Height { get; set; }


    public double X { get; set; }

    public double Y { get; set; }


    public bool ShowLegend { get; set; }
    

    public PanelUpdate ToUpdate(Guid panelId) =>
        new(panelId)
        {
            Height = Height,
            Width = Width,

            X = X,
            Y = Y,

            ShowLegend = ShowLegend,
        };
}


public sealed class EditDashBoardViewModel
{
    public Dictionary<Guid, PanelSettingsViewModel> Panels { get; set; }


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