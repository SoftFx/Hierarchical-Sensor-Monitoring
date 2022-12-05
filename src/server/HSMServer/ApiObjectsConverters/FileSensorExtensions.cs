using HSMSensorDataObjects.FullDataObject;
using System.Text;

namespace HSMServer.ApiObjectsConverters
{
    public static class FileSensorExtensions
    {
        public static FileSensorBytesValue ConvertToFileSensorBytes(this FileSensorValue sensorValue) =>
            new()
            {
                Key = sensorValue.Key,
                Path = sensorValue.Path,
                Time = sensorValue.Time,
                Comment = sensorValue.Comment,
                Status = sensorValue.Status,
                Description = sensorValue.Description,
                Extension = sensorValue.Extension,
                FileContent = Encoding.UTF8.GetBytes(sensorValue.FileContent),
                FileName = sensorValue.FileName,
            };
    }
}
