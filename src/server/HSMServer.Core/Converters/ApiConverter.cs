using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;

namespace HSMServer.Core.Converters
{
    public static class ApiConverter
    {
        public static BooleanValue ConvertToValue(this BoolSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static IntegerValue ConvertToValue(this IntSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static DoubleValue ConvertToValue(this DoubleSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static StringValue ConvertToValue(this StringSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static FileValue ConvertToValue(this FileSensorBytesValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value,
                Name = value.FileName,
                Extension = value.Extension,
                OriginalSize = value.Value.LongLength
            };

        public static IntegerBarValue ConvertToValue(this IntBarSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue
            };

        public static DoubleBarValue ConvertToValue(this DoubleBarSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue
            };

        public static StringValue ConvertToValue(this UnitedSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };
    }
}
