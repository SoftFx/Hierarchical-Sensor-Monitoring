using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using HSMServer.Datasources.Lines;
using System;

namespace HSMServer.Datasources
{
    public sealed class VersionSensorLineDatasource : BaseLineDatasource<VersionValue, Version, Version>
    {
        protected override BaseDataAggregator BuildDataAggregator(Func<BaseValue, Version> converter)
        {
            throw new NotImplementedException();
        }

        protected override Version ConvertToChartType(Version value)
        {
            throw new NotImplementedException();
        }

        protected override Func<VersionValue, Version> GetPropertyFactory(PlottedProperty property)
        {
            throw new NotImplementedException();
        }
    }
}