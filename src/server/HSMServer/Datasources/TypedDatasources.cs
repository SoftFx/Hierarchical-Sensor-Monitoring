using HSMServer.Core.Model;
using System;

namespace HSMServer.Datasources
{
    public sealed class TimespanDatasource : BaseLineDatasource<TimeSpanValue, TimeSpan, long>
    {
        protected override ChartType AggregatedType { get; } = ChartType.Line;

        protected override ChartType NormalType { get; } = ChartType.Line;

        //protected override void AddVisibleValue(BaseValue baseValue)
        //{
        //    throw new NotImplementedException();
        //}

        //protected override void ApplyToLast(BaseValue newValue)
        //{
        //    throw new NotImplementedException();
        //}

        protected override long GetTargetValue(TimeSpanValue value) => value.Value.Ticks;

        //protected override BaseChartValue Convert(BaseValue baseValue) =>
        //    baseValue is TimeSpanValue time ? new TimeSpanChartValue(time) : null;
    }


    public sealed class PointDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.StackedBars;

        protected override ChartType NormalType => ChartType.Points;


        protected override void AddVisibleValue(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }

        protected override void ApplyToLast(BaseValue newValue)
        {
            throw new NotImplementedException();
        }
    }


    public sealed class BarsDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.Bars;

        protected override ChartType NormalType => ChartType.Bars;


        protected override void AddVisibleValue(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }

        protected override void ApplyToLast(BaseValue newValue)
        {
            throw new NotImplementedException();
        }
    }
}
