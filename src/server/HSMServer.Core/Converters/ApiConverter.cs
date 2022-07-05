using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;

namespace HSMServer.Core.Converters
{
    public static class ApiConverter
    {
        public static BooleanValue Convert(this BoolSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static IntegerValue Convert(this IntSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static DoubleValue Convert(this DoubleSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static StringValue Convert(this StringSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static FileValue Convert(this FileSensorBytesValue value) =>
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

        public static IntegerBarValue Convert(this IntBarSensorValue value) =>
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

        public static DoubleBarValue Convert(this DoubleBarSensorValue value) =>
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

        public static StringValue Decode(this UnitedSensorValue value) =>
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
