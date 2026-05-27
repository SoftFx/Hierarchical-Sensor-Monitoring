using System;
using System.Threading.Tasks;


namespace HSMDataCollector.DefaultSensors
{
    public interface ISensor : IDisposable
    {
        string SensorPath { get; }
        ValueTask<bool> InitAsync();
        ValueTask<bool> StartAsync();
        ValueTask StopAsync();
    }
}