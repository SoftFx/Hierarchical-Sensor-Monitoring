using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.Dashboards;

public sealed class PanelViewModel
{
    private const string TypeError = "Can't plot using {0} sensor type";
    private const string UnitError = "Can't plot using {0} unit type";
    private const string DefaultName = "New Panel";

    public CGuidDict<DatasourceViewModel> Sources { get; }


    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DashboardId { get; set; }


    [Display(Name = "Panel:")]
    public string Name { get; set; }

    public string Description { get; set; }


    public SensorType? MainSensorType { get; set; }

    public Unit? MainUnit { get; set; }


    public PanelSettings Settings { get; set; }


    public PanelViewModel() { }

    public PanelViewModel(Panel panel, Guid dashboardId)
    {
        Name = panel.Name ?? DefaultName;
        Description = panel.Description;
        Id = panel.Id;
        DashboardId = dashboardId;
        Settings = panel.Settings;

        Sources = new CGuidDict<DatasourceViewModel>(panel.Sources.ToDictionary(y => y.Key, x => new DatasourceViewModel(x.Value)));
    }


    public async Task<PanelViewModel> InitPanelData(DateTime? from = null)
    {
        await Task.WhenAll(Sources.Values.Select(t => t.LoadDataFrom(from)));

        return this;
    }


    public bool TryAddSource(SensorNodeViewModel source, out string message)
    {
        if (IsSupported(source.Type, source.SelectedUnit, out message))
        {
            MainSensorType = source.Type;
            MainUnit = source.SelectedUnit;
            message = string.Empty;
            return true;
        }

        return false;
    }

    public void UpdateSources(SensorNodeViewModel source)
    {
        if (IsSupported(source.Type, source.SelectedUnit, out _))
            Sources.TryAdd(source.Id, new DatasourceViewModel() { Id = Guid.NewGuid(), SensorId = source.Id });
    }


    public bool IsSupported(SensorType type, Unit? unit, out string error) => IsSupportedType(type, out error) && IsSupportedUnit(unit, out error);


    private bool IsSupportedType(SensorType type, out string message)
    {
        var result = IsSupportedType(type) && MainSensorType.IsNullOrEqual(type);

        message = !result ? string.Format(TypeError, type.ToString()) : string.Empty;

        return result;
    }

    private bool IsSupportedUnit(Unit? unit, out string message)
    {
        var result = MainUnit.IsNullOrEqual(unit);

        message = !result ? string.Format(UnitError, unit.ToString()) : string.Empty;

        return result;
    }

    private static bool IsSupportedType(SensorType type) => type is SensorType.Integer or SensorType.Double or SensorType.TimeSpan;
}