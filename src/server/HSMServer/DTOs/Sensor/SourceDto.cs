using System;
using System.Collections.Generic;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.DTOs.Sensor;

public class SourceDto
{
    public string Name { get; set; }
    
    public List<object> Values { get; set; }

    public SensorInfoDTO SensorInfo { get; set; }
    
    public Guid PanelId { get; set; }

    public SourceDto() {}

    public SourceDto(SensorNodeViewModel sensor, List<object> values, Guid panelId)
    {
        SensorInfo = new SensorInfoDTO(sensor.Type, sensor.Type, sensor.SelectedUnit.ToString());
        Name = sensor.Name;
        Values = values;
        PanelId = panelId;
    }
}