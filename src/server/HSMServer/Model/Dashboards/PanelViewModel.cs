using System;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using HSMCommon.Collections;
namespace HSMServer.Model.Dashboards;

public class PanelViewModel
{
    private const string TypeError = "Can't plot using {0} sensor type";
    private const string UnitError = "Can't plot using {0} unit type";

    private readonly CHash<Guid> _sources = new();


    public Guid Id { get; set; } = Guid.NewGuid();

    public SensorType? SensorType { get; set; }

    public Unit? UnitType { get; set; }
    

    public bool TryAddSource(SensorNodeViewModel source, out string message)
    {
        if (IsSuits(source.Type, source.SelectedUnit, out message))
        {
            SensorType = source.Type;
            UnitType = source.SelectedUnit;
            message = string.Empty;
            return true;
        }

        return false;
    }

    public void UpdateSources(SensorNodeViewModel source)
    {
        if (IsSuits(source.Type, source.SelectedUnit, out _))
            _sources.Add(source.Id);
    }


    public bool IsSuits(SensorType type, Unit? unit, out string errorMessage) =>
        IsTypeSuits(type, out errorMessage) && IsUnitSuits(unit, out errorMessage);

    public bool IsTypeSuits(SensorType type, out string message)
    {
        var result = IsValidType(type) && (!SensorType.HasValue || SensorType.Value == type);

        message = !result ? string.Format(TypeError, type.ToString()) : string.Empty;

        return result;
    }

    public bool IsUnitSuits(Unit? unit, out string message)
    {
        var result = !UnitType.HasValue || UnitType.Value == unit;
        message = !result ? string.Format(UnitError, unit.ToString()) : string.Empty;

        return result;
    }

    private bool IsValidType(SensorType type) => type != Core.Model.SensorType.File &&
                                                 type != Core.Model.SensorType.Enum &&
                                                 type != Core.Model.SensorType.String &&
                                                 type != Core.Model.SensorType.DoubleBar &&
                                                 type != Core.Model.SensorType.IntegerBar;
}