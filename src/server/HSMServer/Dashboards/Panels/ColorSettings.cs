using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Dashboards;

public sealed class ColorSettings
{
    public static Color[] DefaultColors { get; } =
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

    public List<Color> Colors { get; set; } = [..DefaultColors];

    public void FromEntity(ColorSettingsEntity entityColorSettings)
    {
        foreach (var color in entityColorSettings.Colors)
        {
            Colors.Add(Color.FromName(color));
        }
    }

    public ColorSettingsEntity ToEntity() =>
        new()
        {
            Colors = Colors.Select(x => x.Name).ToList()
        };

    public void Update(PanelUpdate update)
    {
        if (update.ColorSettings?.RestoreDefault != null && update.ColorSettings.RestoreDefault.Value)
            Colors = [..DefaultColors];

        if (update.ColorSettings?.Colors != null)
            foreach (var color in update.ColorSettings.Colors)
                Colors.Add(Color.FromName(color));
    }
}