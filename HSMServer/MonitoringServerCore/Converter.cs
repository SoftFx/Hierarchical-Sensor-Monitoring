using System;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using HSMServer.DataLayer.Model;
using HSMServer.DataLayer.Model.TypedDataObjects;
using HSMServer.Model;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public static class Converter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        #region Convert to database objects

        public static SensorDataObject ConvertToDatabase(JobResult jobResult)
        {
            SensorDataObject result = new SensorDataObject();
            result.DataType = SensorDataTypes.JobSensor;
            result.Path = jobResult.Path;
            result.Time = jobResult.Time;
            result.Timestamp = GetTimestamp(result.Time);
            TypedJobSensorData typedData = new TypedJobSensorData { Success = jobResult.Success };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }
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

        public static SensorInfo ConvertToInfo(NewJobResult newJobResult)
        {
            SensorInfo result = new SensorInfo();
            result.Path = newJobResult.Path;
            result.ProductName = newJobResult.ProductName;
            result.SensorName = newJobResult.SensorName;
            return result;
        }

        #endregion


        #region Convert to update messages

        public static SensorUpdateMessage Convert(NewJobResult newJobResult)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Server = newJobResult.ProductName;
            result.Name = newJobResult.SensorName;
            result.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
            result.Path = newJobResult.Path;
            TypedJobSensorData typedData = new TypedJobSensorData { Success = newJobResult.Success };
            result.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(typedData)));
            return result;
        }
        public static SensorUpdateMessage Convert(JobResult jobResult)
        {
            SensorUpdateMessage update = new SensorUpdateMessage();
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
            update.Path = jobResult.Path;
            string server;
            string sensor;
            ExtractServerAndSensor(update.Path, out server, out sensor);
            update.Server = server;
            update.Name = sensor;
            TypedJobSensorData data = new TypedJobSensorData
            {
                Comment =  jobResult.Comment,
                Success =  jobResult.Success
            };
            update.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(data)));
            return update;
        }

        #endregion


        #region Sub-methods

        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        public static void ExtractServerAndSensor(string path, out string server, out string sensor)
        {
            server = string.Empty;
            sensor = string.Empty;
            var splitRes = path.Split("/".ToCharArray());
            server = splitRes[0];
            sensor = splitRes[^1];
        }

        #endregion

    }
}
