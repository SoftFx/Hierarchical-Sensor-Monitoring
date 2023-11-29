using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.DTOs.SensorInfo;
using HSMServer.Extensions;

namespace HSMServer.Model.Dashboards;

public class DatasourceViewModel
{
    public Guid Id { get; set; }

    public Guid SensorId { get; set; }

    public SensorType Type { get; set; }

    public Unit? Unit { get; set; }

    public string Color { get; set; }

    public string Path { get; set; }

    public string Label { get; set; }

    public List<object> Values { get; set; }

    public SensorInfoDto SensorInfo { get; set; }


    public DatasourceViewModel() { }

    public DatasourceViewModel(PanelDatasource dataSource)
    {
        var result = dataSource.Source.Initialize().GetAwaiter().GetResult();

        Id = dataSource.Id;
        SensorId = dataSource.SensorId;
        Color = dataSource.Color.ToRGB();
        Label = dataSource.Label;
        (Path, Type, Unit) = dataSource.Source.GetSourceInfo();
        Values = result.Values.Cast<object>().ToList();
        SensorInfo = new SensorInfoDto(Type, Type, Unit?.ToString());
    }
}