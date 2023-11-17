using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSourceEntity> Sources { get; init; } = new();

        public PanelSettingsEntity Settings { get; set; }
    }


    public record PanelSettingsEntity
    {
        public double Width { get; set; } = 0.3;

        public double Height { get; set; } = 0.2;


        public double X { get; set; }

        public double Y { get; set; }


        public bool ShowLegend { get; set; } = true;
    }
}