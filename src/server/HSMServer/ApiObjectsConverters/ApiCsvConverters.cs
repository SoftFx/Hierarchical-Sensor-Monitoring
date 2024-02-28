using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ApiObjectsConverters
{
    [Flags]
    internal enum ExportOptions : byte
    {
        Simple = 0,
        Hidden = 1,
        Aggregated = 2,
        EmaStatistics = 4,
    }


    internal sealed record Header
    {
        private readonly Func<BaseValue, string> _getPropertyName;


        public string DisplayName { get; }

        public string PropertyName { get; }

        internal bool IsTime => PropertyName.Contains("time", StringComparison.InvariantCultureIgnoreCase);


        public Header(string displayName, string propertyName, Func<BaseValue, string> getPropFunc = null)
        {
            _getPropertyName = getPropFunc;
            PropertyName = propertyName;
            DisplayName = displayName;
        }

        public Header(string name)
        {
            PropertyName = name;
            DisplayName = name;
        }


        public string GetPropertyName(BaseValue value) => _getPropertyName?.Invoke(value) ?? PropertyName;
    }


    internal static class ApiCsvConverters
    {
        private static readonly string _columnSeparator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;

        private static readonly JsonSerializerOptions _serializerOptions = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        private static readonly Func<BaseValue, string> _baseLastUpdateLambda = value => value.LastReceivingTime is null ? nameof(BaseValue.ReceivingTime) : nameof(BaseValue.LastReceivingTime);

        private static readonly Dictionary<Header, ExportOptions> _simpleSensorHeader = new()
        {
            { new("Date", nameof(SensorValueBase.Time)), ExportOptions.Simple },
            { new("Last update time", nameof(BaseValue.LastUpdateTime), value => _baseLastUpdateLambda(value)), ExportOptions.Aggregated },
            { new("Receiving time", nameof(BaseValue.ReceivingTime)), ExportOptions.Hidden },
            { new("Aggregated values count", nameof(BaseValue.AggregatedValuesCount)), ExportOptions.Aggregated },
            { new(nameof(BoolSensorValue.Value)), ExportOptions.Simple },
            { new(nameof(IntegerValue.EmaValue)), ExportOptions.EmaStatistics },
            { new(nameof(SensorValueBase.Status)), ExportOptions.Simple },
            { new(nameof(SensorValueBase.Comment)), ExportOptions.Simple }
        };

        private static readonly Dictionary<Header, ExportOptions> _barSensorHeader = new()
        {
            { new("Open time", nameof(IntBarSensorValue.OpenTime)), ExportOptions.Simple },
            { new("Last update time", nameof(DoubleBarValue.Time)), ExportOptions.Hidden },
            { new("Close time", nameof(DoubleBarValue.CloseTime)), ExportOptions.Hidden },
            { new("Receiving time", nameof(BaseValue.ReceivingTime)), ExportOptions.Hidden },
            { new("First value", nameof(IntBarSensorValue.FirstValue)), ExportOptions.Hidden },
            { new("Last value", nameof(IntBarSensorValue.LastValue)), ExportOptions.Hidden },
            { new(nameof(IntBarSensorValue.Min)), ExportOptions.Simple },
            { new(nameof(IntBarSensorValue.Mean)), ExportOptions.Simple },
            { new(nameof(IntBarSensorValue.Max)), ExportOptions.Simple },
            { new(nameof(IntBarSensorValue.Count)), ExportOptions.Simple },
            { new(nameof(IntegerBarValue.EmaMin)), ExportOptions.EmaStatistics },
            { new(nameof(IntegerBarValue.EmaMean)), ExportOptions.EmaStatistics },
            { new(nameof(IntegerBarValue.EmaMax)), ExportOptions.EmaStatistics },
            { new(nameof(IntegerBarValue.EmaCount)), ExportOptions.EmaStatistics },
            { new(nameof(SensorValueBase.Status)), ExportOptions.Simple },
            { new(nameof(SensorValueBase.Comment)), ExportOptions.Simple },
        };

        private static readonly List<Header> _fileSensorHeader = new()
        {
            new(nameof(SensorValueBase.Time)),
            new(nameof(FileSensorValue.Value)),
            new(nameof(FileSensorValue.Name)),
            new(nameof(FileSensorValue.Extension)),
            new(nameof(SensorValueBase.Status)),
            new(nameof(SensorValueBase.Comment)),
        };

        private static readonly HashSet<string> _validProperties = new()
        {
            nameof(DoubleBarValue.Time),
            nameof(IntBarSensorValue.OpenTime),
            nameof(DoubleBarValue.CloseTime),
            nameof(BaseValue.ReceivingTime),
            nameof(BaseValue.Comment),
        };

        static ApiCsvConverters()
        {
            _serializerOptions.Converters.Add(new JsonStringEnumConverter());
        }


        internal static string ConvertToCsv(this List<BaseValue> values, ExportOptions options = ExportOptions.Hidden)
        {
            if ((values?.Count ?? 0) == 0)
                return string.Empty;

            var content = new StringBuilder(1 << 7);
            var header = values.GetHeader(options);

            content.AppendLine(header.Select(x => x.DisplayName).ToList().BuildRow());

            var rowValues = new List<string>(header.Count);
            foreach (var value in values.OrderByDescending(x => x.Time))
            {
                var rowValue = value is FileValue fileValue ? fileValue.DecompressContent() : value; // TODO smth with this crutch
                var properties = JsonSerializer.SerializeToElement<object>(rowValue, _serializerOptions);

                foreach (var column in header)
                {
                    var jsonPropertyName = column.GetPropertyName(value);
                    var propValue = properties.GetProperty(jsonPropertyName).ToString();

                    rowValues.Add(GetCsvValue(column, value, propValue));
                }

                content.AppendLine(rowValues.BuildRow());
                rowValues.Clear();
            }

            return content.ToString();

            static string GetTransformedValue(Header column, BaseValue value, string propValue)
            {
                if ((column.IsTime || (value is TimeSpanValue && column.PropertyName == nameof(TimeSpanValue.Value))) && DateTime.TryParse(propValue, out var dateTime))
                    return dateTime.ToDefaultFormat();

                if (value.IsTimeout && !_validProperties.TryGetValue(column.PropertyName, out _))
                    return string.Empty;

                //TODO: should be removed after removing SensorStatusJsonConverter
                if (column.PropertyName == nameof(BaseValue.Status) && Enum.TryParse<SensorStatus>(propValue, out var status))
                    return status.ToString();

                return propValue;
            }

            static string GetCsvValue(Header column, BaseValue value, string propValue) => $"""
                                                                                            "{GetTransformedValue(column, value, propValue)}"
                                                                                            """;
        }

        private static List<Header> GetHeader(this List<BaseValue> values, ExportOptions options)
        {
            return values[0] switch
            {
                BooleanValue or IntegerValue or DoubleValue or StringValue or VersionValue or TimeSpanValue or CounterValue =>
                    _simpleSensorHeader.Where(x => options.HasFlag(x.Value)).Select(x => x.Key).ToList(),
                IntegerBarValue or DoubleBarValue =>
                    _barSensorHeader.Where(x => options.HasFlag(x.Value)).Select(x => x.Key).ToList(),
                FileValue => _fileSensorHeader,
                _ => new List<Header>()
            };
        }

        private static string BuildRow(this List<string> values) => string.Join(_columnSeparator, values);
    }
}