using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies;

public interface IPolicy<T> where T : IPolicyCondition
{
    public List<T> Conditions { get; }
    
    public TimeIntervalModel Sensitivity { get; protected set; }
    
    public SensorStatus Status { get; protected set; }

    public string Template { get; protected set; }

    public string Icon { get; protected set; }
}