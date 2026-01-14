using System;
using HSMCommon.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using HSMServer.Datasources.Lines;


namespace HSMServer.Datasources
{
    public sealed class VersionSensorLineDatasource : BaseLineDatasource<VersionValue, Version, Version>
    {
        protected override BaseDataAggregator DataAggregator { get; } = new VersionDataAggregator();


        protected override Version ConvertToChartType(Version value) => value;

        protected override Func<VersionValue, Version> GetPropertyFactory(PlottedProperty property) =>
            GetValuePropertyFactory<VersionValue, Version>(property);
    }
}