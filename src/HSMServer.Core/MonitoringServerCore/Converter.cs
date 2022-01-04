using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Sensor;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

namespace HSMServer.Core.MonitoringServerCore
{
    public class Converter : IConverter
    {
        public Converter(ILogger<Converter> logger)
        {
        }

        public BarSensorValueBase GetBarSensorValue(UnitedSensorValue value)
        {
            BarSensorValueBase result;
            switch (value.Type)
            {
                case SensorType.DoubleBarSensor:
                    result = new DoubleBarSensorValue();
                    CopyCommonFields(value, result);
                    CopyDoubleBarData(value, (DoubleBarSensorValue) result);
                    return result;
                case SensorType.IntegerBarSensor:
                    result = new IntBarSensorValue();
                    CopyCommonFields(value, result);
                    CopyIntBarData(value, (IntBarSensorValue) result);
                    return result;
            }

            return null;
        }

        private void CopyCommonFields(UnitedSensorValue unitedObj, BarSensorValueBase barObj)
        {
            barObj.Comment = unitedObj.Comment;
            barObj.Path = unitedObj.Path;
            barObj.Description = unitedObj.Description;
            barObj.Status = unitedObj.Status;
            barObj.Key = unitedObj.Key;
            barObj.Time = unitedObj.Time;
        }

        private void CopyIntBarData(UnitedSensorValue unitedObj, IntBarSensorValue intBarObj)
        {
            try
            {
                IntBarData data = JsonSerializer.Deserialize<IntBarData>(unitedObj.Data);
                intBarObj.Max = data.Max;
                intBarObj.Mean = data.Mean;
                intBarObj.Min = data.Min;
                intBarObj.Percentiles = data.Percentiles;
                intBarObj.LastValue = data.LastValue;
                intBarObj.Count = data.Count;
                intBarObj.StartTime = data.StartTime;
                intBarObj.EndTime = data.EndTime;
            }
            catch (Exception e)
            { }
        }
        private void CopyDoubleBarData(UnitedSensorValue unitedObj, DoubleBarSensorValue intBarObj)
        {
            try
            {
                DoubleBarData data = JsonSerializer.Deserialize<DoubleBarData>(unitedObj.Data);
                intBarObj.Max = data.Max;
                intBarObj.Mean = data.Mean;
                intBarObj.Min = data.Min;
                intBarObj.Percentiles = data.Percentiles;
                intBarObj.LastValue = data.LastValue;
                intBarObj.Count = data.Count;
                intBarObj.StartTime = data.StartTime;
                intBarObj.EndTime = data.EndTime;
            }
            catch (Exception e)
            { }
        }
    }
}
