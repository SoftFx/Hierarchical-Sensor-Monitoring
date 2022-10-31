using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    internal static class ApiCsvConverters
    {
        private static readonly string _columnSeparator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
        private static readonly JsonSerializerOptions _serializerOptions = new();

        private static readonly List<string> _simpleSensorHeader = new()
        {
            nameof(SensorValueBase.Comment),
            nameof(SensorValueBase.Time),
            nameof(SensorValueBase.Status),
            nameof(BoolSensorValue.Value),
        };

        private static readonly List<string> _barSensorHeader = new()
        {
            nameof(SensorValueBase.Comment),
            nameof(SensorValueBase.Time),
            nameof(SensorValueBase.Status),
            nameof(IntBarSensorValue.OpenTime),
            nameof(IntBarSensorValue.CloseTime),
            nameof(IntBarSensorValue.Count),
            nameof(IntBarSensorValue.Min),
            nameof(IntBarSensorValue.Max),
            nameof(IntBarSensorValue.Mean),
            nameof(IntBarSensorValue.LastValue),
        };

        private static readonly List<string> _fileSensorHeader = new()
        {
            nameof(SensorValueBase.Comment),
            nameof(SensorValueBase.Time),
            nameof(SensorValueBase.Status),
            nameof(BoolSensorValue.Value),
            nameof(FileSensorBytesValue.Extension),
            nameof(FileSensorBytesValue.FileName),
        };


        static ApiCsvConverters()
        {
            _serializerOptions.Converters.Add(new JsonStringEnumConverter());
        }


        internal static string ConvertToCsv(this List<BaseValue> values)
        {
            if ((values?.Count ?? 0) == 0)
                return string.Empty;

            var content = new StringBuilder(1 << 7);
            var header = values.GetHeader();

            content.AppendLine(header.GetRow());

            foreach (var value in values)
            {
                var rowValues = new List<string>(header.Count);
                var properties = JsonSerializer.SerializeToElement((object)value, _serializerOptions);

                foreach (var column in header)
                    rowValues.Add(properties.GetProperty(column).ToString());

                content.AppendLine(rowValues.GetRow());
            }

            return content.ToString();
        }

        private static List<string> GetHeader(this List<BaseValue> values) =>
            values[0] switch
            {
                BooleanValue => _simpleSensorHeader,
                IntegerValue => _simpleSensorHeader,
                DoubleValue => _simpleSensorHeader,
                StringValue => _simpleSensorHeader,
                IntegerBarValue => _barSensorHeader,
                DoubleBarValue => _barSensorHeader,
                FileValue => _fileSensorHeader,
                _ => new(),
            };

        private static string GetRow(this List<string> values) => string.Join(_columnSeparator, values);
    }
}
