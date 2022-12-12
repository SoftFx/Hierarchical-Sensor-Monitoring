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
            nameof(SensorValueBase.Time),
            nameof(BoolSensorValue.Value),
            nameof(SensorValueBase.Status),
            nameof(SensorValueBase.Comment),
        };

        private static readonly List<string> _barSensorHeader = new()
        {
            nameof(SensorValueBase.Time),
            nameof(IntBarSensorValue.OpenTime),
            nameof(IntBarSensorValue.CloseTime),
            nameof(IntBarSensorValue.Min),
            nameof(IntBarSensorValue.Max),
            nameof(IntBarSensorValue.Mean),
            nameof(IntBarSensorValue.Count),
            nameof(IntBarSensorValue.LastValue),
            nameof(SensorValueBase.Status),
            nameof(SensorValueBase.Comment),
        };

        private static readonly List<string> _fileSensorHeader = new()
        {
            nameof(SensorValueBase.Time),
            nameof(FileSensorValue.Value),
            nameof(FileSensorValue.Name),
            nameof(FileSensorValue.Extension),
            nameof(SensorValueBase.Status),
            nameof(SensorValueBase.Comment),
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

            content.AppendLine(header.BuildRow());

            var rowValues = new List<string>(header.Count);
            foreach (var value in values)
            {
                var properties = JsonSerializer.SerializeToElement<object>(value, _serializerOptions);

                foreach (var column in header)
                    rowValues.Add(properties.GetProperty(column).ToString());

                content.AppendLine(rowValues.BuildRow());
                rowValues.Clear();
            }

            return content.ToString();
        }

        private static List<string> GetHeader(this List<BaseValue> values) =>
            values[0] switch
            {
                BooleanValue or IntegerValue or DoubleValue or StringValue => _simpleSensorHeader,
                IntegerBarValue or DoubleBarValue => _barSensorHeader,
                FileValue => _fileSensorHeader,
                _ => new(),
            };

        private static string BuildRow(this List<string> values) => string.Join(_columnSeparator, values);
    }
}
