using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using HSMServer.DataLayer.Model;
using HSMServer.DataLayer.TypedDataObjects;
using HSMServer.Model;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public static class Converter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static SensorDataObject ConvertToDatabase(NewJobResult newJobResult)
        {
            SensorDataObject result = new SensorDataObject();
            result.DataType = SensorDataTypes.JobSensor;
            result.Path = newJobResult.Path;
            result.Time = newJobResult.Time;
            result.Timestamp = GetTimestamp(newJobResult.Time);
            TypedJobSensorData typedData = new TypedJobSensorData { Success = newJobResult.Success };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorUpdateMessage ConvertToSend(NewJobResult newJobResult)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Server = newJobResult.ServerName;
            result.Name = newJobResult.SensorName;
            result.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
            result.Path = newJobResult.Path;
            TypedJobSensorData typedData = new TypedJobSensorData { Success =  newJobResult.Success };
            result.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(typedData)));
            return result;
        }

        public static SensorInfo ConvertToInfo(NewJobResult newJobResult)
        {
            SensorInfo result = new SensorInfo();
            result.Path = newJobResult.Path;
            result.ServerName = newJobResult.ServerName;
            result.SensorName = newJobResult.SensorName;
            return result;
        }

        #region Sub-methods

        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        #endregion

    }
}
