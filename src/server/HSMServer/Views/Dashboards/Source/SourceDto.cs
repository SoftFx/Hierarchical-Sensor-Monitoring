using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Views.Dashboards.Source;

public class SourceDto
{
    public string Name { get; set; }
    
    public List<object> Values { get; set; }

    public SensorInfoDTO SensorInfo { get; set; }
    
    // public SensorType Type { get; set; }
    //
    // public string Units { get; set; }

    public SourceDto()
    {
        
    }
    
    public SourceDto(SensorNodeViewModel sensor, List<object> values)
    {
        SensorInfo = new SensorInfoDTO(sensor.Type, sensor.Type, sensor.SelectedUnit.ToString());
        Name = sensor.Name;
        // Type = sensor.Type;
        // Units = sensor.SelectedUnit.ToString();
        Values = values;
    }
}