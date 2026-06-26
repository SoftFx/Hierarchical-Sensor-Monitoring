using System;
using System.Threading.Tasks;
using HSMSensorDataObjects;


namespace HSMDataCollector.DefaultSensors
{
    public interface ISensor : IDisposable
    {
        string SensorPath { get; }
        ValueTask<bool> InitAsync();
        ValueTask<bool> StartAsync();
        ValueTask StopAsync();
    }

    internal interface ISensorIdentity
    {
        SensorType Type { get; }
        bool IsLastValue { get; }
    }
}
