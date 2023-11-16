using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity
{
    public record DashboardPanelEntity : BaseServerEntity
    {
        public List<PanelSourceEntity> Sources { get; init; } = new();

        public PanelPositionEntity Position { get; set; }
    }


    public record PanelPositionEntity
    {
        public double Width { get; set; } = 0.3;

        public double Height { get; set; } = 0.2;


        public double X { get; set; }

        public double Y { get; set; }
    }
}