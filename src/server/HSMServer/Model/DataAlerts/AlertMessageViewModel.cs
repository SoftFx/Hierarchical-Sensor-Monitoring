using System;
using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Model.DataAlerts;

public class AlertMessageViewModel
{
    public Guid EntityId { get; set; }
    
    public AlertProperty Property { get; set; }

    public PolicyOperation? Operation { get; set; }

    public string Emoji { get; set; }
    
    public string Comment { get; set; }
    
    public string Target { get; set; }


    public string BuildToastMessage(BaseSensorModel sensor)
    {
        var alert = BuildTest(sensor.LastValue, sensor, Comment);
        alert.Operation = Operation.GetDisplayName();
        alert.Target = Target;

        return $"{Emoji} {alert.BuildComment()}";
    }
    
    private static AlertState BuildTest(BaseValue value, BaseSensorModel sensor, string raw)
    {
        var state = value.Type switch
        {
            SensorType.Integer => AlertState.Build((BaseValue<int>)value, sensor),
            SensorType.Double => AlertState.Build((BaseValue<double>)value, sensor),
            SensorType.DoubleBar => AlertState.Build((BarBaseValue<double>)value, sensor),
            SensorType.IntegerBar => AlertState.Build((BarBaseValue<int>)value, sensor),
            _ => AlertState.BuildBase(value, sensor)
        };

        state.Template = AlertState.BuildSystemTemplate(raw);
            
        return state;
    }
}