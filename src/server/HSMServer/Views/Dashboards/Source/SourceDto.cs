using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Views.Dashboards.Source;

public class SourceDto
{
    public string Name { get; set; }
    
    public List<BaseValue> Values { get; set; }
    
    public SensorType Type { get; set; }
    
    public string Units { get; set; }

    public SourceDto()
    {
        
    }
    
    public SourceDto(SensorNodeViewModel sensor, List<BaseValue> values)
    {
        Name = sensor.Name;
        Type = sensor.Type;
        Units = sensor.SelectedUnit.ToString();
        Values = values;
    }
}