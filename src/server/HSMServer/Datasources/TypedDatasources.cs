using HSMServer.Core.Model;
using System;

namespace HSMServer.Datasources
{
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
