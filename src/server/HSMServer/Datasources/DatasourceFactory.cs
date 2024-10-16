﻿using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static bool IsSupportedPlotProperty(BaseSensorModel sensor, PlottedProperty property)
            => sensor.Type switch
            {
                SensorType.IntegerBar or SensorType.DoubleBar => property.IsBarView() || property.IsBarLine() || property.IsBarIntLine() || property.IsBarDoubleLine(),
                SensorType.Integer or SensorType.Double or SensorType.Rate or SensorType.Enum => property.IsInstantView() || property.IsInstantDoubleLine(),
                SensorType.TimeSpan or SensorType.Version => property.IsInstantView(),

                _ => false,
            };


        public static SensorDatasourceBase Build(BaseSensorModel sensor, SourceSettings settings)
        {
            var property = settings.Property;

            SensorDatasourceBase source = sensor.Type switch
            {
                SensorType.Integer when property.IsInstantView() => new IntLineDatasource(),
                SensorType.Integer when property.IsInstantDoubleLine() => new IntToNullDoubleLineDatasource(),

                SensorType.Double when property.IsInstantView() => new DoubleLineDatasource(),
                SensorType.Double when property.IsInstantDoubleLine() => new DoubleToNullDoubleDatasource(),

                SensorType.Rate when property.IsInstantView() => new RateLineDatasource(),
                SensorType.Rate when property.IsInstantDoubleLine() => new RateToNullDoubleDatasource(),

                SensorType.TimeSpan when property.IsInstantView() => new TimespanLineDatasource(),

                SensorType.IntegerBar when property.IsBarView() => new BarsDatasource(),
                SensorType.IntegerBar when property.IsBarLine() => new IntBarLineDatasource(),
                SensorType.IntegerBar when property.IsBarIntLine() => new IntBarIntLineSource(),
                SensorType.IntegerBar when property.IsBarDoubleLine() => new IntBarNullDoubleSource(),

                SensorType.DoubleBar when property.IsBarView() => new BarsDatasource(),
                SensorType.DoubleBar when property.IsBarLine() => new DoubleBarLineDatasource(),
                SensorType.DoubleBar when property.IsBarIntLine() => new DoubleBarIntLineSource(),
                SensorType.DoubleBar when property.IsBarDoubleLine() => new DoubleBarNullDoubleSource(),

                SensorType.Version when property.IsInstantView() => new VersionSensorLineDatasource(),

                SensorType.Boolean => new PointDatasource(),

                SensorType.Enum when property.IsInstantView() => new EnumLineDatasource(),
                SensorType.Enum when property.IsInstantDoubleLine() => new EnumToNullDoubleLineDatasource(),

                _ => throw new Exception($"History visualization for {sensor.Type} sensor by {property} is not supported")
            };

            return source.AttachSensor(sensor, settings);
        }


        private static bool IsInstantView(this PlottedProperty property) => property is PlottedProperty.Value;

        private static bool IsInstantDoubleLine(this PlottedProperty property) => property is PlottedProperty.EmaValue;


        private static bool IsBarView(this PlottedProperty property) => property is PlottedProperty.Bar;

        private static bool IsBarLine(this PlottedProperty property) => property is PlottedProperty.Min or PlottedProperty.Mean or PlottedProperty.Max;

        private static bool IsBarIntLine(this PlottedProperty property) => property is PlottedProperty.Count;

        private static bool IsBarDoubleLine(this PlottedProperty property) => property is PlottedProperty.EmaMin or
            PlottedProperty.EmaMean or PlottedProperty.EmaMax or PlottedProperty.EmaCount;
    }
}