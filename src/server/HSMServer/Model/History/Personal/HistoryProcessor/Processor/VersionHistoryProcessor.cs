using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMServer.Controllers;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Model.History;

internal class VersionHistoryProcessor : HistoryProcessorBase
{
    private readonly VersionSensorLineDatasource _source = new ();
    private static readonly PanelRangeSettings _panelRangeSettings = new ()
    {
        AutoScale = true,
    };
    private readonly SourceSettings _sourceSettings = new ()
    {
        Property = PlottedProperty.Value,
        YRange = _panelRangeSettings,
        SensorType = SensorType.Version,
        AggregateValues = false,
    };


    public HistoryProcessorBase AttachSensor(BaseSensorModel sensor)
    {
        _source.AttachSensor(sensor, _sourceSettings);

        return this;
    }
    
    public override JsonResult GetResultFromValues(SensorNodeViewModel sensor, List<BaseValue> values, int compressedValuesCount)
    {
        return new JsonResult(new
        {
            values = _source.InitializeStatic(values).Select(object (x) => x)
        }, DashboardsController.SerializerOptions);
    }
}