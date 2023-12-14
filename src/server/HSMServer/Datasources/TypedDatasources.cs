using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public sealed class LineDatasource<T> : SensorDatasourceBase where T : INumber<T>
    {
        protected override ChartType AggregatedType => ChartType.Line;

        protected override ChartType NormalType => ChartType.Line;


        protected override BaseChartValue Convert(BaseValue rawValue) =>
            rawValue is BaseValue<T> value ? new LineChartValue<T>(value) : null;
    }


    public sealed class TimespanDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType { get; } = ChartType.Line;

        protected override ChartType NormalType { get; } = ChartType.Line;


        protected override BaseChartValue Convert(BaseValue baseValue) =>
            baseValue is TimeSpanValue time ? new TimeSpanChartValue(time) : null;
    }


    public sealed class PointDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.StackedBars;

        protected override ChartType NormalType => ChartType.Points;


        protected override BaseChartValue Convert(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }
    }


    public sealed class BarsDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.Bars;

        protected override ChartType NormalType => ChartType.Bars;


        protected override BaseChartValue Convert(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }
    }
}
