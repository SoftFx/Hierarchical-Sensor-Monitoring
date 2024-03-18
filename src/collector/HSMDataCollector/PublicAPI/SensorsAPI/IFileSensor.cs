using HSMSensorDataObjects;
using System.Threading.Tasks;

namespace HSMDataCollector.PublicInterface
{
    public interface IFileSensor : IInstantValueSensor<string>
    {
        Task<bool> SendFile(string filePath, SensorStatus status = SensorStatus.Ok, string comment = "");
    }
}