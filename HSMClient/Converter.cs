using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMClientWPFControls.Objects;
using SensorsService;

namespace HSMClient
{
    public static class Converter
    {
        public static MonitoringSensorUpdate Convert(SensorUpdateMessage updateMessage)
        {
            MonitoringSensorUpdate result = new MonitoringSensorUpdate();
            result.Product = updateMessage.Product;
            result.ActionType = Convert(updateMessage.ActionType);
            result.Name = updateMessage.Name;
            result.Path = ConvertSensorPath(updateMessage.Path);
            result.SensorType = Convert(updateMessage.ObjectType);
            result.DataObject = updateMessage.DataObject.ToByteArray();
            result.Time = updateMessage.Time.ToDateTime();
            return result;
        }

        public static ActionTypes Convert(SensorUpdateMessage.Types.TransactionType transactionType)
        {
            switch (transactionType)
            {
                case SensorUpdateMessage.Types.TransactionType.TransAdd:
                    return ActionTypes.Add;
                case SensorUpdateMessage.Types.TransactionType.TransNone:
                    return ActionTypes.None;
                case SensorUpdateMessage.Types.TransactionType.TransRemove:
                    return ActionTypes.Remove;
                case SensorUpdateMessage.Types.TransactionType.TransUpdate:
                    return ActionTypes.Update;
            }
            throw new Exception($"Unknown transaction type: {transactionType}!");
        }

        public static SensorTypes Convert(SensorUpdateMessage.Types.SensorObjectType type)
        {
            switch (type)
            {
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor:
                    return SensorTypes.JobSensor;
            }
            throw new Exception($"Unknown sensor type: {type}!");
        }

        public static List<string> ConvertSensorPath(string path)
        {
            return path.Split(new[] {'/'}).ToList();
        }
    }
}
