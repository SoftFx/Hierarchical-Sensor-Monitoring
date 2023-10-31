using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using HSMCommon.Collections;
namespace HSMServer.Model.Dashboards;

public class PanelViewModel
{
    private readonly CHash<Guid> _sources = new ();


    public Guid Id { get; set; } = Guid.NewGuid();

    public SensorType? SensorType { get; set; }
    
    public Unit? UnitType { get; set; }

    public bool TryAddSource(SensorNodeViewModel source)
    {
        if (IsSuits(source.Type, source.SelectedUnit))
        {
            SensorType = source.Type;
            UnitType = source.SelectedUnit;

            return true;
        }

        return false;
    }

    public void UpdateSources(SensorNodeViewModel source)
    {
        if (IsSuits(source.Type, source.SelectedUnit))
            _sources.Add(source.Id);
    }


    public bool IsSuits(SensorType type, Unit? unit) => IsTypeSuits(type) && IsUnitSuits(unit);
    public bool IsTypeSuits(SensorType type) => !SensorType.HasValue || SensorType.Value == type;
    
    public bool IsUnitSuits(Unit? unit) => !UnitType.HasValue || UnitType.Value == unit;
}