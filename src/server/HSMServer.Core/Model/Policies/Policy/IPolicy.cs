using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies;

public interface IPolicy<T> where T : IPolicyCondition
{
    public List<T> Conditions { get; }
    
    public TimeIntervalModel Sensitivity { get; }
    
    public SensorStatus Status { get; }

    public string Template { get; }

    public string Icon { get; }
}