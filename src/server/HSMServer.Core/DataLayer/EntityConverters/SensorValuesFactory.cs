using HSMServer.Core.Model;
using System.Text.Json;

namespace HSMServer.Core.DataLayer
{
    internal static class SensorValuesFactory
    {
        private const string TimePropertyName = "Time";
        private const string RecievingTimePropertyName = "TimeCollected";
        private const string StatusPropertyName = "Status";
        private const string TypedDataPropertyName = "TypedData";

        private const string CommentPropertyName = "Comment";

        private const string BoolValuePropertyName = "BoolValue";
        private const string IntValuePropertyName = "IntValue";
        private const string DoubleValuePropertyName = "DoubleValue";
        private const string StringValuePropertyName = "StringValue";

        private const string FileContentPropertyName = "FileContent";
        private const string FileNamePropertyName = "FileName";
        private const string ExtensionPropertyName = "Extension";
        private const string OriginalSizePropertyName = "OriginalFileSensorContentSize";

        private const string CountPropertyName = "Count";
        private const string OpenTimePropertyName = "StartTime";
        private const string CloseTimePropertyName = "EndTime";
        private const string MinPropertyName = "Min";
        private const string MaxPropertyName = "Max";
        private const string MeanPropertyName = "Mean";
        private const string LastValuePropertyName = "LastValue";


        internal static BaseValue BuildValue<T>(JsonElement rootElement)
        {
            var valueType = typeof(T).Name;

            return valueType switch
            {
                nameof(BooleanValue) => BuildBooleanValue(rootElement),
                nameof(IntegerValue) => BuildIntegerValue(rootElement),
                nameof(DoubleValue) => BuildDoubleValue(rootElement),
                nameof(StringValue) => BuildStringValue(rootElement),
                nameof(FileValue) => BuildFileValue(rootElement),
                nameof(IntegerBarValue) => BuildIntegerBarValue(rootElement),
                nameof(DoubleBarValue) => BuildDoubleBarValue(rootElement),
                _ => null,
            };
        }


        private static BooleanValue BuildBooleanValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Value = GetTypedData(element).ReadBool(BoolValuePropertyName),
            };

        private static IntegerValue BuildIntegerValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Value = GetTypedData(element).ReadInt(IntValuePropertyName),
            };

        private static DoubleValue BuildDoubleValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Value = GetTypedData(element).ReadDouble(DoubleValuePropertyName),
            };

        private static StringValue BuildStringValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Value = GetTypedData(element).ReadString(StringValuePropertyName),
            };

        private static FileValue BuildFileValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Value = GetTypedData(element).ReadBytes(FileContentPropertyName),
                Name = GetTypedData(element).ReadString(FileNamePropertyName),
                Extension = GetTypedData(element).ReadString(ExtensionPropertyName),
                OriginalSize = element.ReadLong(OriginalSizePropertyName),
            };

        private static IntegerBarValue BuildIntegerBarValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Count = GetTypedData(element).ReadInt(CountPropertyName),
                OpenTime = GetTypedData(element).ReadDateTime(OpenTimePropertyName),
                CloseTime = GetTypedData(element).ReadDateTime(CloseTimePropertyName),
                Min = GetTypedData(element).ReadInt(MinPropertyName),
                Max = GetTypedData(element).ReadInt(MaxPropertyName),
                Mean = GetTypedData(element).ReadInt(MeanPropertyName),
                LastValue = GetTypedData(element).ReadInt(LastValuePropertyName),
            };

        private static DoubleBarValue BuildDoubleBarValue(JsonElement element) =>
            new()
            {
                Time = element.ReadDateTime(TimePropertyName),
                ReceivingTime = element.ReadDateTime(RecievingTimePropertyName),
                Status = GetStatus(element),
                Comment = GetTypedData(element).ReadString(CommentPropertyName),
                Count = GetTypedData(element).ReadInt(CountPropertyName),
                OpenTime = GetTypedData(element).ReadDateTime(OpenTimePropertyName),
                CloseTime = GetTypedData(element).ReadDateTime(CloseTimePropertyName),
                Min = GetTypedData(element).ReadDouble(MinPropertyName),
                Max = GetTypedData(element).ReadDouble(MaxPropertyName),
                Mean = GetTypedData(element).ReadDouble(MeanPropertyName),
                LastValue = GetTypedData(element).ReadDouble(LastValuePropertyName),
            };

        private static SensorStatus GetStatus(JsonElement element)
        {
            var status = element.ReadByte(StatusPropertyName);

            return status switch
            {
                1 => SensorStatus.Ok,
                2 => SensorStatus.Warning,
                3 => SensorStatus.Error,
                _ => SensorStatus.Unknown,
            };
        }

        private static JsonElement GetTypedData(JsonElement element) =>
            JsonDocument.Parse(element.ReadString(TypedDataPropertyName)).RootElement;
    }
}
