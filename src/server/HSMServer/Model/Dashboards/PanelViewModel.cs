using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.Dashboards;

public sealed class PanelViewModel
{
    private const string DefaultName = "New Panel";

    public CDict<DatasourceViewModel> Sources { get; }

    public CDict<TemplateViewModel> Templates { get; }


    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DashboardId { get; set; }


    [Display(Name = "Panel:")]
    public string Name { get; set; }

    [Display(Name = "Show product names")]
    public bool ShowProduct { get; set; }

    public string Description { get; set; }

    [Display(Name = "Aggregate values")]
    public bool AggregateValues { get; set; }


    public SensorType? MainSensorType { get; set; }

    public Unit? MainUnit { get; set; }


    public DateTime LastUpdate { get; set; } = DateTime.MinValue;

    public PanelRangeSettings YRange { get; set; }

    public PanelSettings Settings { get; set; }


    public PanelViewModel() { }

    public PanelViewModel(Panel panel, Guid dashboardId, Dictionary<Guid, string> availableFolders)
    {
        MainSensorType = panel.MainSensorType;
        Name = panel.Name ?? DefaultName;
        Description = panel.Description;
        ShowProduct = panel.ShowProduct;
        AggregateValues = panel.AggregateValues;
        Id = panel.Id;
        DashboardId = dashboardId;

        Settings = panel.Settings;
        YRange = panel.YRange;

        Sources = new CDict<DatasourceViewModel>(panel.Sources.ToDictionary(y => y.Value.Id, x => BuildSourceViewModel(x.Value)));
        Templates = new CDict<TemplateViewModel>(panel.Subscriptions.ToDictionary(y => y.Value.Id, x => new TemplateViewModel(x.Value, availableFolders)));

        DatasourceViewModel BuildSourceViewModel(PanelDatasource source)
        {
            if (source.Sensor.LastUpdate > LastUpdate)
                LastUpdate = source.Sensor.LastUpdate;

            return new DatasourceViewModel(source, ShowProduct);
        }
    }


    public async Task<PanelViewModel> InitPanelData(DateTime? from = null)
    {
        await Task.WhenAll(Sources.Values.Select(t => t.LoadDataFrom(Settings.IsSingleMode ? null : from)));

        return this;
    }
}