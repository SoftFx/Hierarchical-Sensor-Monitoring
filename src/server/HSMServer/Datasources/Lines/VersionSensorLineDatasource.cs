using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using HSMServer.Datasources.Lines;
using System;

namespace HSMServer.Datasources
{
    public sealed class VersionSensorLineDatasource : BaseLineDatasource<VersionValue, Version, Version>
    {
        protected override BaseDataAggregator BuildDataAggregator(Func<BaseValue, Version> converter) =>
            new VersionDataAggregator(converter);


        protected override Version ConvertToChartType(Version value) => value;

        protected override Func<VersionValue, Version> GetPropertyFactory(PlottedProperty property) =>
            GetValuePropertyFactory<VersionValue, Version>(property);
    }
}