using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Core.Model;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    // Pure DTO mapping for the sensor-tree read surface (#1386). No authorization,
    // no filtering — the controllers decide WHAT is mapped; this class only decides
    // HOW a live model renders on the wire. All timestamps render as UTC (ISO 8601
    // with the Z designator): model times are UtcNow-sourced ticks without a Kind,
    // so the mapper pins the kind instead of relying on serialization defaults.
    public static class SensorTreeDtoMapper
    {
        public static ProductDto ToProductDto(ProductModel product) => new()
        {
            Id = product.Id,
            Name = product.DisplayName,
            Description = product.Description,
            CreationDate = AsUtc(product.CreationDate),
        };


        public static NodeDto ToNodeDto(ProductModel node) => new()
        {
            Id = node.Id,
            Name = node.DisplayName,
            Description = node.Description,
            Type = node.IsRoot ? NodeTypeProduct : NodeTypeFolder,
            Path = node.FullPath,
            CreationDate = AsUtc(node.CreationDate),
            Parent = node.Parent is { } parent ? ToNodeRef(parent) : null,
            Folders = node.SubProducts.Values
                .OrderBy(folder => folder.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => folder.Id)
                .Select(ToNodeRef)
                .ToList(),
            Sensors = node.Sensors.Values
                .OrderBy(sensor => sensor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sensor => sensor.Id)
                .Select(ToSensorRef)
                .ToList(),
        };


        public static NodeRefDto ToNodeRef(ProductModel node) => new()
        {
            Id = node.Id,
            Name = node.DisplayName,
        };


        public static SensorRefDto ToSensorRef(BaseSensorModel sensor) => new()
        {
            Id = sensor.Id,
            Name = sensor.DisplayName,
            Type = sensor.Type.ToString(),
        };


        // The one sensor shape for list and item endpoints alike (see SensorDto):
        // the current value is embedded so a search hit needs no second request.
        // Parentless sensors cannot resolve product/parent refs — the controller's
        // visibility pass drops them before mapping; defensive nulls here keep the
        // mapper total anyway.
        public static SensorDto ToSensorDto(BaseSensorModel sensor) => new()
        {
            Id = sensor.Id,
            Path = sensor.FullPath,
            Name = sensor.DisplayName,
            Description = sensor.Description,
            Type = sensor.Type.ToString(),
            Unit = UnitOf(sensor),
            Status = sensor.Status?.Status.ToString(),
            State = sensor.State.ToString(),
            CreationDate = AsUtc(sensor.CreationDate),
            LastUpdate = sensor.HasData ? AsUtc(sensor.LastUpdate) : null,
            Product = sensor.Parent?.Root is { } product ? ToNodeRef(product) : null,
            Parent = sensor.Parent is { } parent ? ToNodeRef(parent) : null,
            LastValue = ToValueDto(sensor, sensor.LastValue),
            EnumOptions = sensor.Type == SensorType.Enum ? ToEnumOptions(sensor) : null,
        };


        public static SensorValueDto ToValueDto(BaseSensorModel sensor, BaseValue value)
        {
            if (value is null)
                return null;

            return new SensorValueDto
            {
                Time = AsUtc(value.Time),
                Status = value.Status.ToString(),
                Comment = value.Comment,
                Value = BoxValue(sensor, value),
            };
        }


        // The polymorphic `value` of the envelope, by sensor value type. File values
        // render metadata only — the byte[] content never reaches a response.
        private static object BoxValue(BaseSensorModel sensor, BaseValue value) => value switch
        {
            FileValue file => new FileValueDto
            {
                Name = file.Name,
                Extension = file.Extension,
                Size = file.OriginalSize,
            },

            EnumValue enumValue => new EnumValueDto
            {
                Value = enumValue.Value,
                Label = sensor.EnumOptions.TryGetValue(enumValue.Value, out var option) ? option.Value : null,
            },

            BarBaseValue<int> intBar => new BarValueDto
            {
                Min = intBar.Min,
                Max = intBar.Max,
                Mean = intBar.Mean,
                Count = intBar.Count,
            },

            BarBaseValue<double> doubleBar => new BarValueDto
            {
                Min = doubleBar.Min,
                Max = doubleBar.Max,
                Mean = doubleBar.Mean,
                Count = doubleBar.Count,
            },

            BaseValue<bool> boolean => boolean.Value,
            BaseValue<int> integer => integer.Value,
            BaseValue<double> number => number.Value,
            BaseValue<string> text => text.Value,
            TimeSpanValue span => span.Value.ToString(),
            VersionValue version => version.Value?.ToString(),

            _ => value.ShortInfo,
        };


        private static List<SensorEnumOptionDto> ToEnumOptions(BaseSensorModel sensor) =>
            sensor.EnumOptions
                .OrderBy(option => option.Key)
                .Select(option => new SensorEnumOptionDto
                {
                    Value = option.Key,
                    Label = option.Value.Value,
                    Description = option.Value.Description,
                })
                .ToList();


        // Unit precedence: the collector-declared unit of the value (any numeric
        // sensor can carry one), then the Rate display denominator; null otherwise.
        private static string UnitOf(BaseSensorModel sensor)
        {
            if (sensor.OriginalUnit is { } unit)
                return unit.GetDisplayName();

            if (sensor.Type == SensorType.Rate)
                return (sensor.DisplayUnit ?? RateDisplayUnit.PerSecond).GetDisplayName();

            return null;
        }


        // Model timestamps are UTC-sourced ticks without a Kind (pin them);
        // a Local-kind instant (binder-produced) converts instead of relabeling.
        private static DateTime AsUtc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Utc => time,
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        };


        public const string NodeTypeProduct = "product";
        public const string NodeTypeFolder = "folder";
    }
}
