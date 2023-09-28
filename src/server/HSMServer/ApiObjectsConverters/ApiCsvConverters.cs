using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    internal static class ApiCsvConverters
    {
        private static readonly string _columnSeparator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
        private static readonly JsonSerializerOptions _serializerOptions = new()
        { 
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals 
        };

        private static readonly List<string> _simpleSensorHeader = new()
        {
            nameof(SensorValueBase.Time),
            nameof(BoolSensorValue.Value),
            nameof(SensorValueBase.Status),
            nameof(SensorValueBase.Comment),
        };

        private static readonly List<string> _barSensorHeader = new()
        {
            nameof(IntBarSensorValue.OpenTime),
            nameof(IntBarSensorValue.Min),
            nameof(IntBarSensorValue.Mean),
            nameof(IntBarSensorValue.Max),
            nameof(IntBarSensorValue.Count),
            nameof(IntBarSensorValue.LastValue),
            nameof(SensorValueBase.Status),
            nameof(SensorValueBase.Comment),
        };

        private static readonly List<string> _simpleHiddenHeader = new()
        {
            nameof(BaseValue.AggregatedValuesCount)
        };
        
        private static readonly List<string> _barHiddenHeader = new()
        {
            nameof(DoubleBarValue.Time),
            nameof(DoubleBarValue.CloseTime)
        };

        private static readonly List<string> _defaultHiddenHeader = new()
        {
            nameof(BaseValue.ReceivingTime)
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


        internal static string ConvertToCsv(this List<BaseValue> values, bool addHiddenColumns = true)
        {
            if ((values?.Count ?? 0) == 0)
                return string.Empty;

            var content = new StringBuilder(1 << 7);
            var header = values.GetHeader(addHiddenColumns);

            content.AppendLine(header.BuildRow());

            var rowValues = new List<string>(header.Count);
            foreach (var value in values)
            {
                var rowValue = value is FileValue fileValue ? fileValue.DecompressContent() : value; // TODO smth with this crutch
                var properties = JsonSerializer.SerializeToElement<object>(rowValue, _serializerOptions);

                foreach (var column in header)
                    rowValues.Add(properties.GetProperty(column).ToString());

                content.AppendLine(rowValues.BuildRow());
                rowValues.Clear();
            }

            return content.ToString();
        }

        private static List<string> GetHeader(this List<BaseValue> values, bool addHiddenColumns)
        {
            return values[0] switch
            {
                BooleanValue or IntegerValue or DoubleValue or StringValue or VersionValue or TimeSpanValue => addHiddenColumns
                    ? _simpleSensorHeader.Concat(_defaultHiddenHeader).Concat(_simpleHiddenHeader).ToList()
                    : _simpleSensorHeader,
                IntegerBarValue or DoubleBarValue => addHiddenColumns
                    ? _barSensorHeader.Concat(_defaultHiddenHeader).Concat(_barHiddenHeader).ToList()
                    : _barSensorHeader,
                FileValue => _fileSensorHeader,
                _ => new List<string>()
            };
        }

        private static string BuildRow(this List<string> values) => string.Join(_columnSeparator, values);
    }
}
