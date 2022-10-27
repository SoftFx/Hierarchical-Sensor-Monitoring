using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using System;
using System.Collections.Generic;

namespace HSMServer.ApiObjectsConverters
{
    internal static class ApiCsvConverters
    {
        internal static string ConvertToCsv(this List<BaseValue> values)
        {
            if ((values?.Count ?? 0) == 0)
                return string.Empty;

            var content = values[0] switch
            {
                BooleanValue => values.SimpleSensorsToCsv<bool>(),
                IntegerValue => values.SimpleSensorsToCsv<int>(),
                DoubleValue => values.SimpleSensorsToCsv<double>(),
                StringValue => values.SimpleSensorsToCsv<string>(),
                IntegerBarValue => values.BarSensorsToCsv<int>(),
                DoubleBarValue => values.BarSensorsToCsv<double>(),
                FileValue => values.FileSensorsToCsv(),
                _ => string.Empty,
            };

            return content;
        }

        private static string SimpleSensorsToCsv<T>(this List<BaseValue> values) =>
            SensorsToCsv(values,
                         () => GetSimpleSensorCsvHeader<ValueBase<T>>(),
                         apiValue => GetSimpleSensorCsvRow((ValueBase<T>)apiValue));

        private static string BarSensorsToCsv<T>(this List<BaseValue> values) =>
            SensorsToCsv(values,
                         () => GetBarSensorCsvHeader<BarValueSensorBase<T>>(),
                         apiValue => GetBarSensorCsvRow((BarValueSensorBase<T>)apiValue));

        private static string FileSensorsToCsv(this List<BaseValue> values) =>
            SensorsToCsv(values,
                         () => GetFileSensorCsvHeader(),
                         apiValue => GetFileSensorCsvRow((FileSensorBytesValue)apiValue));

        private static string SensorsToCsv(this List<BaseValue> values, Func<List<string>> getHeaderAction, Func<object, Dictionary<string, object>> getRowAction)
        {
            var headers = getHeaderAction();

            var csvWriter = new CsvWriter(headers);

            var rows = new List<string>(values.Count);
            foreach (var value in values)
            {
                var apiValue = value.Convert();
                var rowValues = getRowAction(apiValue);

                rows.Add(csvWriter.GetRow(rowValues));
            }

            return csvWriter.GetContent(rows);
        }


        private static List<string> GetSensorValueCsvHeader() =>
            new(1 << 4)
            {
                nameof(SensorValueBase.Comment),
                nameof(SensorValueBase.Time),
                nameof(SensorValueBase.Status),
            };

        private static List<string> GetSimpleSensorCsvHeader<T>()
        {
            var headers = GetSensorValueCsvHeader();

            headers.Add(nameof(ValueBase<T>.Value));
            return headers;
        }

        private static List<string> GetBarSensorCsvHeader<T>()
        {
            var headers = GetSensorValueCsvHeader();

            headers.Add(nameof(BarValueSensorBase<T>.OpenTime));
            headers.Add(nameof(BarValueSensorBase<T>.CloseTime));
            headers.Add(nameof(BarValueSensorBase<T>.Count));
            headers.Add(nameof(BarValueSensorBase<T>.Min));
            headers.Add(nameof(BarValueSensorBase<T>.Max));
            headers.Add(nameof(BarValueSensorBase<T>.Mean));
            headers.Add(nameof(BarValueSensorBase<T>.LastValue));

            return headers;
        }

        private static List<string> GetFileSensorCsvHeader()
        {
            var headers = GetSimpleSensorCsvHeader<FileSensorBytesValue>();

            headers.Add(nameof(FileSensorBytesValue.Extension));
            headers.Add(nameof(FileSensorBytesValue.FileName));

            return headers;
        }


        private static Dictionary<string, object> GetSensorValueCsvRow(SensorValueBase value) =>
            new(1 << 4)
            {
                { nameof(SensorValueBase.Comment), value.Comment },
                { nameof(SensorValueBase.Time), value.Time },
                { nameof(SensorValueBase.Status), value.Status },
            };

        private static Dictionary<string, object> GetSimpleSensorCsvRow<T>(ValueBase<T> value)
        {
            var row = GetSensorValueCsvRow(value);

            row.Add(nameof(ValueBase<T>.Value), value.Value);

            return row;
        }

        private static Dictionary<string, object> GetBarSensorCsvRow<T>(BarValueSensorBase<T> value)
        {
            var row = GetSensorValueCsvRow(value);

            row.Add(nameof(BarValueSensorBase<T>.OpenTime), value.OpenTime);
            row.Add(nameof(BarValueSensorBase<T>.CloseTime), value.CloseTime);
            row.Add(nameof(BarValueSensorBase<T>.Count), value.Count);
            row.Add(nameof(BarValueSensorBase<T>.Min), value.Min);
            row.Add(nameof(BarValueSensorBase<T>.Max), value.Max);
            row.Add(nameof(BarValueSensorBase<T>.Mean), value.Mean);
            row.Add(nameof(BarValueSensorBase<T>.LastValue), value.LastValue);

            return row;
        }

        private static Dictionary<string, object> GetFileSensorCsvRow(FileSensorBytesValue value)
        {
            var row = GetSimpleSensorCsvRow(value);

            row.Add(nameof(FileSensorBytesValue.Extension), value.Extension);
            row.Add(nameof(FileSensorBytesValue.FileName), value.FileName);

            return row;
        }
    }
}
