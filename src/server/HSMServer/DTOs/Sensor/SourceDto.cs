using HSMServer.Dashboards;
using HSMServer.Datasources;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.DTOs.Sensor;

public class SourceDto
{
    public Guid SensorId { get; set; }

    public Guid Id { get; set; }


    public string Path { get; set; }


    public string Label { get; set; }

    public string Color { get; set; }


    public List<object> Values { get; set; }

    public SensorInfoDto SensorInfo { get; set; }

    

    public SourceDto() { }

    public SourceDto(InitChartSourceResponse chartResponse, PanelDatasource source)
    {
        var sensor = source.Sensor;

        Color = source.Color.ToRGB();
        SensorInfo = new SensorInfoDto(sensor.Type, sensor.Type, sensor.OriginalUnit.ToString());
        Id = source.Id;
        SensorId = sensor.Id;
        Label = sensor.DisplayName;
        Path = sensor.FullPath;
        Values = chartResponse.Values;
    }
}