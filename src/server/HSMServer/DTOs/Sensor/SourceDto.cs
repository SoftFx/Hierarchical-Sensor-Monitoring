using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Dashboards;
using HSMServer.Datasources;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.DTOs.Sensor;

public class SourceDto
{
    public Guid Id { get; set; }
    
    public Guid SensorId { get; set; }

    public string Label { get; set; }

    public string Path { get; set; }
    
    public List<object> Values { get; set; }

    public SensorInfoDto SensorInfo { get; set; }

    public string Color { get; set; }


    public SourceDto() {}

    public SourceDto(InitChartSourceResponse chartResponse, PanelDatasource source, SensorNodeViewModel sensor)
    {
        Color = source.Color.ToRGB();
        SensorInfo = new SensorInfoDto(sensor.Type, sensor.Type, sensor.SelectedUnit.ToString());
        Id = source.Id;
        SensorId = sensor.Id;
        Label = sensor.Name;
        Path = sensor.FullPath;
        Values = chartResponse.Values.Cast<object>().ToList();
    }
}