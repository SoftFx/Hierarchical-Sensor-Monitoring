using HSMServer.Dashboards;
using HSMServer.Datasources;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;

namespace HSMServer.Model.Dashboards;

public class SourceViewModel
{
    public Guid SensorId { get; set; }

    public Guid Id { get; set; }


    public string Path { get; set; }


    public string Label { get; set; }

    public string Color { get; set; }


    public List<object> Values { get; set; }

    public SensorInfoViewModel SensorInfo { get; set; }

    

    public SourceViewModel() { }

    public SourceViewModel(InitChartSourceResponse chartResponse, PanelDatasource source)
    {
        var sensor = source.Sensor;

        Color = source.Color.ToRGB();
        SensorInfo = new SensorInfoViewModel(sensor.Type, sensor.Type, sensor.OriginalUnit.ToString());
        Id = source.Id;
        SensorId = sensor.Id;
        Label = sensor.DisplayName;
        Path = sensor.FullPath;
        Values = chartResponse.Values;
    }
}