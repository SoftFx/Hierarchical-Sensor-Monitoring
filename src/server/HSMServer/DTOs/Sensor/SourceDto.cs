using System;
using System.Collections.Generic;
using System.Drawing;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.DTOs.Sensor;

public class SourceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Path { get; set; }
    
    public List<object> Values { get; set; }

    public SensorInfoDto SensorInfo { get; set; }

    public Guid PanelId { get; set; }

    public string Color { get; set; }


    public SourceDto() {}

    public SourceDto(SensorNodeViewModel sensor, List<object> values, Guid panelId, Guid sourceId, Color color)
    {
        Color = color.ToRGB();
        SensorInfo = new SensorInfoDto(sensor.Type, sensor.Type, sensor.SelectedUnit.ToString());
        Name = sensor.Name;
        Path = sensor.FullPath;
        Values = values;
        PanelId = panelId;
        Id = sourceId;
    }
}