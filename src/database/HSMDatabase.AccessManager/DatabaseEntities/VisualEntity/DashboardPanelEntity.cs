using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSourceEntity> Sources { get; init; } = new();

        public PanelSettingsEntity Settings { get; set; }

        public bool IsAggregate { get; set; } = true;

        public bool ShowProduct { get; set; }
    }


    public record PanelSettingsEntity
    {
        public double Width { get; init; }

        public double Height { get; init; }


        public double X { get; init; }

        public double Y { get; init; }


        public bool ShowLegend { get; init; }
    }
}