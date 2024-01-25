using HSMCommon.Extensions;
using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Model.Dashboards;

public class DatasourceViewModel
{
    private readonly PanelDatasource _panelSource;

    private static readonly List<PlottedProperty> _singleSensorProperties =
    [
        PlottedProperty.Value
    ];

    private static readonly List<PlottedProperty> _singleEmaSensorProperties =
    [
        PlottedProperty.EmaValue
    ];

    private static readonly List<PlottedProperty> _barSensorProperties =
    [
        PlottedProperty.Min,
        PlottedProperty.Mean,
        PlottedProperty.Max,
        PlottedProperty.Count,
    ];

    private static readonly List<PlottedProperty> _barEmaSensorProperties =
    [
        PlottedProperty.EmaMin,
        PlottedProperty.EmaMean,
        PlottedProperty.EmaMax,
        PlottedProperty.EmaCount,
    ];


    public List<SelectListItem> AvailableProperties { get; set; }

    public SensorInfoViewModel SensorInfo { get; set; }

    public List<object> Values { get; set; } = new();

    public ChartType ChartType { get; set; }

    public string ProductName { get; set; }

    public string SensorName { get; set; }

    public SensorType Type { get; set; }

    public Guid SensorId { get; set; }

    public string Path { get; set; }

    public Unit? Unit { get; set; }

    public Guid Id { get; set; }


    public PlottedProperty Property { get; set; }

    public string Label { get; set; }

    public string Color { get; set; }


    public bool ShowProduct { get; }

    public string DisplayLabel => ShowProduct ? $"{ProductName}: {Label}" : Label;


    public DatasourceViewModel() { }

    public DatasourceViewModel(PanelDatasource source, bool showProduct)
    {
        _panelSource = source;
        ShowProduct = showProduct;

        Id = source.Id;
        SensorId = source.SensorId;
        Color = source.Color.ToRGB();
        Label = source.Label;

        var sensor = source.Sensor;

        Path = sensor.FullPath;
        ProductName = sensor.RootProductName;
        SensorName = sensor.DisplayName;
        Unit = sensor.OriginalUnit;
        Type = sensor.Type;

        SensorInfo = new SensorInfoViewModel(Type, Type, Unit?.GetDisplayName());

        AvailableProperties = GetAvailableProperties(sensor);
        Property = source.Property;
    }

    public DatasourceViewModel(InitChartSourceResponse chartResponse, PanelDatasource source, bool showProduct) : this(source, showProduct)
    {
        ChartType = chartResponse.ChartType;
        Values = chartResponse.Values;
    }


    public async Task LoadDataFrom(DateTime? from)
    {
        var task = from is null ? _panelSource.Source.Initialize() : _panelSource.Source.Initialize(from.Value, DateTime.UtcNow);

        var response = await task;
        Values = response.Values;
        ChartType = response.ChartType;
    }


    private List<SelectListItem> GetAvailableProperties(BaseSensorModel sensor)
    {
        var isBar = sensor is IntegerBarSensorModel or DoubleBarSensorModel;

        var properties = new List<PlottedProperty>(isBar ? _barSensorProperties : _singleSensorProperties);

        if (sensor.Statistics.HasEma())
            properties.AddRange(isBar ? _barEmaSensorProperties : _singleEmaSensorProperties);

        return properties.ToSelectedItems(k => k.GetDisplayName());
    }
}