using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Dashboards;

public sealed class ColorSettings
{
    private static readonly Color[] _defaultColors =
    [
        Color.FromName("#7B2D43"),
        Color.FromName("#629CD6"),
        Color.FromName("#EA7B62"),
        Color.FromName("#2D4E62"),
        Color.FromName("#59C838"),
        Color.FromName("#D22DB2"),
        Color.FromName("#2238EA"),
        Color.FromName("#FFBD17"),
        Color.FromName("#B2592D"),
        Color.FromName("#62C69C"),
    ];

    public List<string> Colors { get; set; } = [.._defaultColors.Select(x => x.Name)];

    public bool IsEnabled { get; set; } = false;

    public void FromEntity(ColorSettingsEntity entityColorSettings)
    {
        Colors = entityColorSettings.Colors;
        IsEnabled = entityColorSettings.IsEnabled;
    }

    public ColorSettingsEntity ToEntity() =>
        new()
        {
            Colors = Colors,
            IsEnabled = IsEnabled
        };

    public ColorSettingsUpdate ToUpdate() =>
        new()
        {
            Colors = Colors,
            IsEnabled = IsEnabled,
        };

    public void Update(PanelUpdate update)
    {
        IsEnabled = update.ColorSettings?.IsEnabled ?? IsEnabled;

        if (update.ColorSettings?.RestoreDefault != null && update.ColorSettings.RestoreDefault.Value)
            Colors = [.._defaultColors.Select(x => x.Name)];

        if (update.ColorSettings?.Colors != null)
            Colors = update.ColorSettings.Colors;
    }
}