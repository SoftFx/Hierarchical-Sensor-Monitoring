using HSMServer.Core.Model;
using HSMServer.Datasources.Aggregators;
using System;

namespace HSMServer.Datasources
{
    public sealed class PointDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.StackedBars;

        protected override ChartType NormalType => ChartType.Points;

        protected override BaseDataAggregator DataAggregator => throw new NotImplementedException();
    }


    public sealed class BarsDatasource : SensorDatasourceBase
    {
        protected override ChartType AggregatedType => ChartType.Bars;

        protected override ChartType NormalType => ChartType.Bars;

        protected override BaseDataAggregator DataAggregator => throw new NotImplementedException();
    }
}
