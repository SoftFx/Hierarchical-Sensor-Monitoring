using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.Dashboards;

public sealed class PanelViewModel
{
    private const string DefaultName = "New Panel";

    public CGuidDict<DatasourceViewModel> Sources { get; }


    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DashboardId { get; set; }


    [Display(Name = "Panel:")]
    public string Name { get; set; }

    [Display(Name = "Display product name in source label")]
    public bool ShowProduct { get; set; }

    public string Description { get; set; }


    public SensorType? MainSensorType { get; set; }

    public Unit? MainUnit { get; set; }


    public PanelSettings Settings { get; set; }


    public PanelViewModel() { }

    public PanelViewModel(Panel panel, Guid dashboardId)
    {
        Name = panel.Name ?? DefaultName;
        Description = panel.Description;
        ShowProduct = panel.ShowProduct;
        Id = panel.Id;
        DashboardId = dashboardId;
        Settings = panel.Settings;

        Sources = new CGuidDict<DatasourceViewModel>(panel.Sources.ToDictionary(y => y.Value.Id, x => new DatasourceViewModel(x.Value, ShowProduct)));
    }


    public async Task<PanelViewModel> InitPanelData(DateTime? from = null)
    {
        await Task.WhenAll(Sources.Values.Select(t => t.LoadDataFrom(from)));

        return this;
    }
}