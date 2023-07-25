using System;
using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Model.DataAlerts;

public class AlertMessageViewModel
{
    public Guid EntityId { get; set; }
    
    public AlertProperty Property { get; set; }

    public PolicyOperation Operation { get; set; }

    public string Emoji { get; set; }
    
    public string Comment { get; set; }
    
    public string Target { get; set; }


    public string BuildToastMessage(BaseSensorModel sensor)
    {
        var test = AlertState.BuildTest(sensor.LastValue, sensor, Comment);
        test.Operation = Operation.GetDisplayName();
        test.Target = Target;

        return $"{Emoji} {test.BuildComment()}";
    }
}