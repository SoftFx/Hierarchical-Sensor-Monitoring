using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.Dashboards;

public class DatasourceViewModel
{
    private readonly PanelDatasource _panelSource;


    public Guid Id { get; set; }

    public Guid SensorId { get; set; }

    public SensorType Type { get; set; }

    public Unit? Unit { get; set; }

    public string Color { get; set; }

    public string Path { get; set; }

    public string Label { get; set; }

    public List<object> Values { get; set; } = new();

    public SensorInfoViewModel SensorInfo { get; set; }


    public DatasourceViewModel() { }

    public DatasourceViewModel(PanelDatasource source)
    {
        _panelSource = source;

        Id = source.Id;
        SensorId = source.SensorId;
        Color = source.Color.ToRGB();
        Label = source.Label;

        var sensor = source.Sensor;

        Path = sensor.FullPath;
        Type = sensor.Type;
        Unit = sensor.OriginalUnit;

        SensorInfo = new SensorInfoViewModel(Type, Type, Unit?.ToString());
    }


    public async Task LoadDataFrom(DateTime? from)
    {
        var task = from is null ? _panelSource.Source.Initialize() : _panelSource.Source.Initialize(from.Value, DateTime.UtcNow);

        Values = (await task).Values;
    }
}